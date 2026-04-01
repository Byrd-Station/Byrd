// SPDX-FileCopyrightText: 2026 Raze500
// SPDX-License-Identifier: AGPL-3.0-or-later

// the event director is omu's single scheduler that replaces the legacy station event schedulers.
// it owns when roundstart / minor / midround / timer rolls happen, but deliberately reuses existing
// gamerule prototypes so all gameplay content stays data-driven and doesn't live in this file.
//
// round structure, as per the design doc ("secret structured" scratchpaper):
//   roundstart  - fires once at round begin, up to 5 reroll attempts if startgamerule fails
//   loop        - minor table rolls immediately at round start, then the loop begins:
//                   wait 5-20 min -> roll timer table -> roll minor table -> repeat
//   midround    - fires once independently at a random time between 40-80 minutes
//
// minor and timer are NOT independent — they share one loop.
// after each timer roll, a minor roll fires immediately, then the 5-20 min delay resets.
//
// all timings and weights live in eventDirectorConfig + eventDirectorTable yaml prototypes.
// to change round pacing, edit the yaml — no c# changes needed.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Omu.Shared.GameTicking.EventDirector;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Omu.Server.GameTicking.EventDirector;

public sealed class EventDirectorSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly ISawmill _sawmill = Logger.GetSawmill("event-director");

    // maximum times roundstart will reroll if startgamerule returns false.
    // eligibility filters catch most invalid rolls before they reach startgamerule,
    // but the cap prevents an infinite loop in edge cases.
    private const int RoundstartMaxAttempts = 5;

    // --- per-round state ---
    // these are reset at the start of every round so no state leaks between rounds

    // every rule the director successfully started this round, in order.
    // stored as (roundTime, ruleId, tableLabel) so admins can see exactly what fired and when.
    private readonly List<(TimeSpan Time, string Rule, string Table)> _history = new();

    // the minor/timer loop shares a single timer.
    // when it fires: roll timer table, then immediately roll minor table, then reschedule.
    private TimeSpan? _nextLoopFireAt;

    // when the single midround event fires (absolute round time, randomized at roundstart)
    private TimeSpan? _nextMidroundRollAt;

    // true once the midround event has already fired this round (it only fires once)
    private bool _midroundTriggered;

    // pausing lets admins freeze the director mid-round without killing the round
    private bool _paused;

    // tracks whether this round was started by the director (used for status reporting)
    private bool _startedThisRound;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Enabled || _paused || _gameTicker.RunLevel != GameRunLevel.InRound)
            return;

        if (!TryGetConfig(out var config))
            return;

        var roundTime = _gameTicker.RoundDuration();

        // midround fires once at its pre-scheduled time and never again this round
        if (!_midroundTriggered && _nextMidroundRollAt is { } nextMidround && roundTime >= nextMidround)
        {
            RollConfiguredTable(config.MidroundTable, "midround");
            _midroundTriggered = true;
        }

        // the minor/timer loop:
        //   when the timer fires → roll timer table, then immediately roll minor table, then reschedule.
        //   this matches the scratchpaper: "roll table 2 → wait 5-20 min → roll table 4 → return to step 2"
        if (_nextLoopFireAt is { } nextLoop && roundTime >= nextLoop)
        {
            RollConfiguredTable(config.TimerTable, "timer");
            RollConfiguredTable(config.MinorTable, "minor");
            ScheduleNextLoop(config, roundTime);
        }
    }

    // fires once at the beginning of the round to kick everything off
    private void OnRoundStarting(RoundStartingEvent ev)
    {
        ResetRoundState();

        if (!Enabled)
            return;

        if (!TryGetConfig(out var config))
            return;

        _startedThisRound = true;
        _sawmill.Info($"event director enabled for round {ev.Id} using config '{config.ID}'.");

        // roll the roundstart antag, retrying up to RoundstartMaxAttempts times if startgamerule fails
        RollRoundstartWithRetries(config);

        // first minor roll fires immediately at round start — no delay
        RollConfiguredTable(config.MinorTable, "minor");

        // schedule the loop (timer roll + minor roll repeat) and the single midround
        ScheduleNextLoop(config, TimeSpan.Zero);
        ScheduleFirstMidround(config);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New != GameRunLevel.InRound)
            ResetRoundState();
    }

    // --- public api used by the admin console command ---

    public bool Enabled => _cfg.GetCVar(CCVars.EventDirectorEnabled);
    public new bool IsPaused => _paused;
    public bool StartedThisRound => _startedThisRound;
    public TimeSpan? NextLoopFireAt => _nextLoopFireAt;
    public TimeSpan? NextMidroundRollAt => _nextMidroundRollAt;
    public bool MidroundTriggered => _midroundTriggered;
    public string ActiveConfigId => _cfg.GetCVar(CCVars.EventDirectorConfig);

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    // switches the active config by id.
    // if called from the lobby, the change is fully clean — the new config takes effect at roundstart.
    // if called mid-round, timers are rescheduled immediately with the new config's delays.
    public bool SetConfig(string configId, out string message)
    {
        if (!_proto.HasIndex<EventDirectorConfigPrototype>(configId))
        {
            message = $"unknown config '{configId}'. make sure the id matches a eventDirectorConfig prototype.";
            return false;
        }

        _cfg.SetCVar(CCVars.EventDirectorConfig, configId);

        // if mid-round, reschedule the loop and midround with the new config's timings.
        // if in lobby, nothing to reschedule — OnRoundStarting will pick up the new config naturally.
        if (_gameTicker.RunLevel == GameRunLevel.InRound && _proto.TryIndex<EventDirectorConfigPrototype>(configId, out var config))
        {
            var roundTime = _gameTicker.RoundDuration();
            ScheduleNextLoop(config, roundTime);

            if (!_midroundTriggered)
                ScheduleFirstMidround(config);

            message = $"config switched to '{configId}' mid-round. loop and midround rescheduled.";
            return true;
        }

        message = $"config set to '{configId}'. will take full effect when the next round starts.";
        return true;
    }

    // returns a human-readable one-liner for the admin 'status' subcommand
    public string BuildStatus()
    {
        var runLevel  = _gameTicker.RunLevel;
        var roundTime = runLevel == GameRunLevel.InRound ? _gameTicker.RoundDuration() : TimeSpan.Zero;

        // nextLoop is when the next timer roll fires — a minor roll immediately follows it
        var nextLoop     = _nextLoopFireAt?.ToString(@"hh\:mm\:ss") ?? "not scheduled";
        var nextMidround = _midroundTriggered ? "already fired" : (_nextMidroundRollAt?.ToString(@"hh\:mm\:ss") ?? "not scheduled");

        return $"enabled={Enabled}, paused={IsPaused}, runLevel={runLevel}, config={ActiveConfigId}, " +
               $"roundStarted={_startedThisRound}, roundTime={roundTime:hh\\:mm\\:ss}, " +
               $"nextLoop={nextLoop}, nextMidround={nextMidround}";
    }

    // lists every entry in a named table with its current eligibility status
    public IEnumerable<string> DescribeTable(string tableName)
    {
        if (!TryResolveNamedTable(tableName, out var table, out var error))
        {
            yield return error;
            yield break;
        }

        var roundTime = _gameTicker.RunLevel == GameRunLevel.InRound ? _gameTicker.RoundDuration() : TimeSpan.Zero;
        var readyPlayers = _gameTicker.ReadyPlayerCount();

        foreach (var entry in table.Entries)
        {
            var name = entry.Name ?? entry.Rule;
            var reason = TryGetIneligibleReason(entry, readyPlayers, roundTime);
            var status = reason == null ? "eligible" : $"blocked: {reason}";
            yield return $"{entry.Id} -> {name} ({entry.Rule}), weight={entry.Weight}, {status}";
        }
    }

    // manually rolls a named table — useful for admin testing mid-round
    public bool RollNamedTable(string tableName, out string message)
    {
        if (!TryResolveNamedTable(tableName, out var table, out var error))
        {
            message = error;
            return false;
        }

        return RollTable(table, tableName, out message);
    }

    // fires a specific gamerule by id, bypassing the table system entirely
    public bool FireRule(string ruleId, out string message)
    {
        if (!_proto.HasIndex<EntityPrototype>(ruleId))
        {
            message = $"unknown gamerule '{ruleId}'.";
            return false;
        }

        var started = _gameTicker.StartGameRule(ruleId);
        message = started
            ? $"started gamerule '{ruleId}'."
            : $"failed to start gamerule '{ruleId}'.";
        return started;
    }

    // --- private scheduling helpers ---

    // rolls the roundstart table up to RoundstartMaxAttempts times.
    // each failed attempt picks a fresh candidate from the eligible pool.
    // if all attempts fail, the round begins with no initial antag — this is intentional fallback behaviour.
    private void RollRoundstartWithRetries(EventDirectorConfigPrototype config)
    {
        if (!_proto.TryIndex(config.RoundStartTable, out var table))
        {
            _sawmill.Error($"roundstart table '{config.RoundStartTable}' was not found.");
            return;
        }

        for (var attempt = 1; attempt <= RoundstartMaxAttempts; attempt++)
        {
            if (RollTable(table, "roundstart", out var message))
            {
                _sawmill.Info(message);
                return;
            }

            _sawmill.Warning($"roundstart attempt {attempt}/{RoundstartMaxAttempts} failed: {message}");
        }

        _sawmill.Warning("roundstart failed all attempts — round begins with no initial antag.");
    }

    // the minor/timer loop uses MinorDelayMin/Max as the shared wait between iterations.
    // minor fires immediately at round start; after that the pattern is:
    //   wait 5-20 min → roll timer → roll minor → repeat
    private void ScheduleNextLoop(EventDirectorConfigPrototype config, TimeSpan fromTime)
    {
        var min = Math.Min(config.MinorDelayMinMinutes, config.MinorDelayMaxMinutes);
        var max = Math.Max(config.MinorDelayMinMinutes, config.MinorDelayMaxMinutes);
        _nextLoopFireAt = fromTime + TimeSpan.FromMinutes(_random.Next(min, max + 1));
    }

    // picks a single random time in the [min, max] window for the midround event.
    // called once at roundstart — after that the midround flag prevents it from firing again.
    private void ScheduleFirstMidround(EventDirectorConfigPrototype config)
    {
        var min = Math.Min(config.FirstMidroundRollMinMinutes, config.FirstMidroundRollMaxMinutes);
        var max = Math.Max(config.FirstMidroundRollMinMinutes, config.FirstMidroundRollMaxMinutes);
        _nextMidroundRollAt = TimeSpan.FromMinutes(_random.Next(min, max + 1));
        _sawmill.Info($"midround event scheduled for {_nextMidroundRollAt:hh\\:mm\\:ss}.");
    }

    // --- private roll helpers ---

    private bool RollConfiguredTable(ProtoId<EventDirectorTablePrototype> tableId, string label)
    {
        if (!_proto.TryIndex(tableId, out var table))
        {
            _sawmill.Error($"event director table '{tableId}' was not found.");
            return false;
        }

        if (!RollTable(table, label, out var message))
        {
            _sawmill.Warning(message);
            return false;
        }

        _sawmill.Info(message);
        return true;
    }

    private bool RollTable(EventDirectorTablePrototype table, string label, out string message)
    {
        var roundTime = _gameTicker.RunLevel == GameRunLevel.InRound ? _gameTicker.RoundDuration() : TimeSpan.Zero;
        var readyPlayers = _gameTicker.ReadyPlayerCount();

        // filter down to entries that pass all eligibility checks
        var candidates = new List<EventDirectorTableEntry>();
        foreach (var entry in table.Entries)
        {
            if (TryGetIneligibleReason(entry, readyPlayers, roundTime) == null)
                candidates.Add(entry);
        }

        if (candidates.Count == 0)
        {
            message = $"event director could not roll '{label}' — table '{table.ID}' had no eligible entries.";
            return false;
        }

        var totalWeight = candidates.Sum(e => MathF.Max(e.Weight, 0f));
        if (totalWeight <= 0f)
        {
            message = $"event director could not roll '{label}' — table '{table.ID}' has no positive weights.";
            return false;
        }

        // weighted random pick
        var pick = _random.NextFloat(totalWeight);
        EventDirectorTableEntry selected = candidates[0];
        foreach (var entry in candidates)
        {
            pick -= MathF.Max(entry.Weight, 0f);
            if (pick > 0f)
                continue;
            selected = entry;
            break;
        }

        var started = _gameTicker.StartGameRule(selected.Rule);
        if (started)
        {
            // record this in the history so admins can see what the director has done this round
            _history.Add((roundTime, selected.Rule, label));
        }

        message = started
            ? $"event director rolled '{label}' from table '{table.ID}' → started '{selected.Rule}' ({selected.Id})."
            : $"event director rolled '{selected.Rule}' from table '{table.ID}', but the gamerule did not start.";
        return started;
    }

    // returns null if the entry is eligible, or a short reason string if it isn't
    private string? TryGetIneligibleReason(EventDirectorTableEntry entry, int readyPlayers, TimeSpan roundTime)
    {
        if (_gameTicker.IsGameRuleActive(entry.Rule))
            return "already active";

        if (readyPlayers < entry.MinimumPlayers)
            return $"needs at least {entry.MinimumPlayers} ready players (have {readyPlayers})";

        if (readyPlayers > entry.MaximumPlayers)
            return $"allows at most {entry.MaximumPlayers} ready players";

        if (roundTime.TotalMinutes < entry.EarliestStartMinutes)
            return $"earliest start is minute {entry.EarliestStartMinutes}";

        if (entry.MaximumOccurrences >= 0 && GetOccurrences(entry.Rule) >= entry.MaximumOccurrences)
            return $"max occurrences ({entry.MaximumOccurrences}) reached";

        if (entry.RepeatDelayMinutes > 0 && GetTimeSinceLastOccurrence(entry.Rule) is { } elapsed
            && elapsed < TimeSpan.FromMinutes(entry.RepeatDelayMinutes))
            return $"repeat delay {entry.RepeatDelayMinutes}m not met";

        return null;
    }

    private int GetOccurrences(string ruleId)
        => _gameTicker.AllPreviousGameRules.Count(rule => rule.Item2 == ruleId);

    private TimeSpan? GetTimeSinceLastOccurrence(string ruleId)
    {
        foreach (var (time, rule) in _gameTicker.AllPreviousGameRules.Reverse())
        {
            if (rule != ruleId)
                continue;
            return _gameTicker.RoundDuration() - time;
        }
        return null;
    }

    // returns the full event history for this round — used by the admin 'history' subcommand
    public IReadOnlyList<(TimeSpan Time, string Rule, string Table)> GetHistory()
        => _history;

    // clears all per-round state so nothing leaks into the next round
    private void ResetRoundState()
    {
        _nextLoopFireAt     = null;
        _nextMidroundRollAt = null;
        _midroundTriggered  = false;
        _paused             = false;
        _startedThisRound   = false;
        _history.Clear();
    }

    // --- prototype lookup helpers ---

    private bool TryGetConfig([NotNullWhen(true)] out EventDirectorConfigPrototype? config)
    {
        var id = ActiveConfigId;
        if (_proto.TryIndex(id, out config))
            return true;

        config = null;
        _sawmill.Error($"event director config '{id}' was not found.");
        return false;
    }

    // resolves "roundstart", "minor", "midround", or "timer" to the matching table prototype
    private bool TryResolveNamedTable(string tableName, [NotNullWhen(true)] out EventDirectorTablePrototype? table, out string error)
    {
        error = string.Empty;
        table = null;

        if (!TryGetConfig(out var config))
        {
            error = $"active config '{ActiveConfigId}' was not found.";
            return false;
        }

        ProtoId<EventDirectorTablePrototype> tableId = tableName.ToLowerInvariant() switch
        {
            "roundstart" => config.RoundStartTable,
            "minor"      => config.MinorTable,
            "midround"   => config.MidroundTable,
            "timer"      => config.TimerTable,
            _            => default
        };

        if (tableId == default)
        {
            error = $"unknown table '{tableName}'. use: roundstart, minor, midround, timer.";
            return false;
        }

        if (_proto.TryIndex(tableId, out table))
            return true;

        table = null;
        error = $"table prototype '{tableId}' was not found.";
        return false;
    }
}
