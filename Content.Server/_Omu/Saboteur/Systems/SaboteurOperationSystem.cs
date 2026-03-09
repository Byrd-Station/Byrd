// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Objectives;
using Content.Server.StationRecords.Systems;
using Content.Server.Store.Systems;
using Content.Shared._Omu.Saboteur;
using Content.Shared.Chat;
using Content.Shared.CriminalRecords;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Systems;

/// <summary>
/// Handles saboteur operation lifecycle: objective assignment, completion detection,
/// reputation tracking, TC grants, tier progression, and exposure checks.
/// </summary>
public sealed class SaboteurOperationSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly ObjectivesSystem _objectives = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly StoreSystem _store = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SaboteurConditionCoreSystem _conditionCore = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    private static readonly Dictionary<SecurityStatus, int> SecuritySeverity = new()
    {
        { SecurityStatus.None, 0 },
        { SecurityStatus.Suspected, 1 },
        { SecurityStatus.Search, 2 },
        { SecurityStatus.Wanted, 3 },
        { SecurityStatus.Detained, 4 },
        { SecurityStatus.Paroled, 5 },
        { SecurityStatus.Discharged, 6 },
        { SecurityStatus.Perma, 7 },
        { SecurityStatus.Dangerous, 8 },
    };

    /// <remarks>Not reentrant-safe; must not be used from callbacks that also use this buffer.</remarks>
    private readonly List<EntProtoId> _scratchObjectivePool = new();
    /// <remarks>Not reentrant-safe; must not be used from callbacks that also use this buffer.</remarks>
    private readonly List<int> _scratchEligibleTiers = new();

    /// <summary>
    /// System-level index of tier → available saboteur operation prototype IDs.
    /// Built once in <see cref="Initialize"/> and refreshed on prototype reload,
    /// so individual rule starts never need to enumerate all entity prototypes.
    /// </summary>
    private Dictionary<int, List<EntProtoId>> _tierObjectivesIndex = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SaboteurComponent, ComponentRemove>(OnSaboteurRemoved);
        SubscribeLocalEvent<SaboteurOperationComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<SaboteurOperationComponent, ObjectiveGetProgressEvent>(OnObjectiveGetProgress);
        SubscribeLocalEvent<SaboteurOperationComponent, RequirementCheckEvent>(OnRequirementCheck);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        RebuildTierObjectivesIndex();
    }

    private void OnSaboteurRemoved(Entity<SaboteurComponent> entity, ref ComponentRemove args)
    {
        if (_mind.TryGetMind(entity, out var mindId, out _))
        {
            RemCompDeferred<SaboteurMindComponent>(mindId);
            Log.Info($"Saboteur cleanup: removed SaboteurMindComponent from mind of {ToPrettyString(entity)}");
        }
    }

    private void OnAfterAssign(Entity<SaboteurOperationComponent> ent, ref ObjectiveAfterAssignEvent args)
    {
        var proto = _prototypeManager.Index(ent.Comp.OperationId);
        var ftlBase = proto.LocId;
        _metaData.SetEntityName(ent, Loc.GetString(ftlBase), args.Meta);
        _metaData.SetEntityDescription(ent, Loc.GetString($"{ftlBase}-desc"), args.Meta);
    }

    private void OnObjectiveGetProgress(Entity<SaboteurOperationComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (IsOperationCompleted(args.MindId, ent.Comp.OperationId))
            args.Progress = 1f;
        else
            args.Progress ??= 0f;
    }

    /// <summary>
    /// Prevents assigning an objective whose operation has already been completed by this saboteur.
    /// Without this check the same finished operation can be re-assigned, and because
    /// <see cref="RunCompletionSweep"/> skips already-completed operations the saboteur
    /// would never receive a follow-up objective.
    /// </summary>
    private void OnRequirementCheck(Entity<SaboteurOperationComponent> ent, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        if (TryComp<SaboteurMindComponent>(args.MindId, out var mindComp)
            && mindComp.CompletedOperations.Contains(ent.Comp.OperationId))
        {
            args.Cancelled = true;
        }
    }

    private bool IsOperationCompleted(EntityUid mindId, ProtoId<SaboteurOperationPrototype> operationId)
    {
        if (TryComp<SaboteurMindComponent>(mindId, out var mindComp))
            return mindComp.CompletedOperations.Contains(operationId);

        return false;
    }

    private bool TryGetMindComp(EntityUid saboteur, out EntityUid mindId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SaboteurMindComponent? mindComp)
    {
        mindId = default;
        mindComp = null;
        if (!_mind.TryGetMind(saboteur, out mindId, out _))
            return false;
        return TryComp(mindId, out mindComp);
    }

    private bool TryGetMindComp(EntityUid saboteur, out EntityUid mindId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SaboteurMindComponent? mindComp,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MindComponent? mind)
    {
        mindId = default;
        mindComp = null;
        mind = null;
        if (!_mind.TryGetMind(saboteur, out mindId, out mind))
            return false;
        return TryComp(mindId, out mindComp);
    }

    /// <summary>
    /// Checks every saboteur's criminal record and applies permanent exposure penalties.
    /// </summary>
    public void RunExposureChecks(Entity<SaboteurRuleComponent?> rule)
    {
        if (!Resolve(rule, ref rule.Comp))
            return;

        var exposureQuery = EntityQueryEnumerator<SaboteurComponent>();
        while (exposureQuery.MoveNext(out var uid, out _))
            CheckExposure(uid, (rule.Owner, rule.Comp));
    }

    /// <summary>
    /// Sweeps all saboteurs' objectives for newly completed operations and rewards them.
    /// </summary>
    public void RunCompletionSweep(Entity<SaboteurRuleComponent?> rule)
    {
        if (!Resolve(rule, ref rule.Comp))
            return;

        if (!_conditionCore.TryGetDirtyTracking(out var dirty))
            return;

        // Flush pending domain dirtiness so IsObjectiveDirty has up-to-date data.
        // We intentionally do NOT early-return when no objectives are dirty,
        // because PendingCompletions from external progress queries (e.g. the
        // character-screen UI) must still be processed.
        _conditionCore.FlushPendingDirty(dirty);

        var completionQuery = EntityQueryEnumerator<SaboteurComponent>();
        while (completionQuery.MoveNext(out var uid, out _))
        {
            if (!_mind.TryGetMind(uid, out var mindId, out var mind))
                continue;

            if (!TryComp<SaboteurMindComponent>(mindId, out var mindComp))
                continue;

            var objectives = mind.Objectives;
            for (var i = 0; i < objectives.Count; i++)
            {
                var objective = objectives[i];
                if (!TryComp<SaboteurOperationComponent>(objective, out var opComp))
                    continue;

                if (mindComp.CompletedOperations.Contains(opComp.OperationId))
                    continue;

                // Skip objectives that are neither dirty nor pending completion.
                // A pending completion means a prior progress evaluation (possibly
                // from an external UI query) already found progress >= 1.0 and
                // recorded it; we must still verify and process the completion.
                if (!_conditionCore.IsObjectiveDirty(dirty, objective)
                    && !_conditionCore.IsCompletionPending(dirty, objective))
                {
                    continue;
                }

                var ev = new ObjectiveGetProgressEvent(mindId, mind);
                RaiseLocalEvent(objective, ref ev);

                // Always clear the pending-completion flag after evaluation,
                // regardless of outcome — the sweep has now processed it.
                _conditionCore.ClearPendingCompletion(dirty, objective);

                if (ev.Progress >= 1f)
                {
                    CompleteOperation(uid, rule, opComp.OperationId, opComp.ReputationGain, opComp.IsMajor);
                }
            }

            // --- Fallback (non-saboteur) objective reward sweep ---
            ProcessFallbackObjectives(uid, rule, mindId, mind, mindComp);
        }
    }

    /// <summary>
    /// Checks non-saboteur objectives assigned to a saboteur for completion and
    /// grants difficulty-scaled TC and reputation rewards. The completed objective
    /// is removed and a new one assigned; each fallback objective is only ever
    /// given once so no separate reward-tracking set is needed.
    /// </summary>
    private void ProcessFallbackObjectives(
        EntityUid saboteur,
        Entity<SaboteurRuleComponent?> rule,
        EntityUid mindId,
        MindComponent mind,
        SaboteurMindComponent mindComp)
    {
        if (!Resolve(rule, ref rule.Comp))
            return;

        // Iterate backwards so we can safely remove entries.
        for (var i = mind.Objectives.Count - 1; i >= 0; i--)
        {
            var objective = mind.Objectives[i];

            // Skip saboteur-specific objectives; those are handled above.
            if (HasComp<SaboteurOperationComponent>(objective))
                continue;

            if (!TryComp<ObjectiveComponent>(objective, out var objComp))
                continue;

            var progressEv = new ObjectiveGetProgressEvent(mindId, mind);
            RaiseLocalEvent(objective, ref progressEv);

            if (progressEv.Progress is not >= 1f)
                continue;

            var difficulty = objComp.Difficulty;
            var tcGain = (int) Math.Floor(difficulty * rule.Comp.FallbackTcPerDifficulty);
            var repGain = (int) Math.Floor(difficulty * rule.Comp.FallbackRepPerDifficulty);

            if (repGain > 0)
                AddReputation((saboteur, null), rule, repGain, mindId, mindComp);

            if (tcGain > 0)
                GrantTelecrystals((saboteur, null), rule, tcGain, mindComp);

            NotifyPlayer(saboteur, Loc.GetString("saboteur-fallback-complete",
                ("rep", repGain),
                ("tc", tcGain),
                ("totalRep", mindComp.Reputation),
                ("tier", SaboteurTierHelper.GetTierName(rule.Comp.Tiers, mindComp.ReputationTier))));

            Log.Info($"Saboteur {ToPrettyString(saboteur)} completed fallback objective " +
                     $"{ToPrettyString(objective)} (difficulty={difficulty}, rep={repGain}, tc={tcGain})");

            // Remove the completed fallback objective and assign a new one.
            _mind.TryRemoveObjective(mindId, mind, i);

            if (!mindComp.GloriousDeathActive)
                AssignNextObjective(saboteur, mindComp);
        }
    }

    private void CheckExposure(EntityUid saboteur, Entity<SaboteurRuleComponent> rule)
    {
        if (!TryGetMindComp(saboteur, out var mindId, out var mindComp))
            return;

        if (mindComp.ExposurePenaltyMultiplier <= rule.Comp.ExposureFloorMultiplier)
            return;

        if (!TryGetCriminalStatus(saboteur, out var currentStatus))
            return;

        if (GetSeverity(currentStatus) > GetSeverity(mindComp.HighestObservedStatus))
            mindComp.HighestObservedStatus = currentStatus;

        var worstMultiplier = Math.Max(
            SaboteurTierHelper.GetExposureMultiplier(rule.Comp.ExposureMultipliers, mindComp.HighestObservedStatus),
            rule.Comp.ExposureFloorMultiplier);

        if (worstMultiplier != mindComp.ExposurePenaltyMultiplier)
        {
            var oldMultiplier = mindComp.ExposurePenaltyMultiplier;
            mindComp.ExposurePenaltyMultiplier = worstMultiplier;

            var penaltyPercent = (int) ((1f - worstMultiplier) * 100);
            NotifyPlayer(saboteur, Loc.GetString("saboteur-exposed",
                ("reason", Loc.GetString(SaboteurTierHelper.GetSecurityStatusLocKey(mindComp.HighestObservedStatus))),
                ("penalty", penaltyPercent)));

            Log.Info($"Saboteur {ToPrettyString(saboteur)} exposure escalated to " +
                     $"{mindComp.HighestObservedStatus} (rep multiplier: {worstMultiplier})");

            var exposureEv = new SaboteurExposureUpdatedEvent(mindId, oldMultiplier, worstMultiplier);
            RaiseLocalEvent(rule.Owner, ref exposureEv);
        }
    }

    /// <summary>
    /// Marks an operation as complete, awards reputation and TC, raises events,
    /// removes the completed objective, and assigns the next one.
    /// </summary>
    public void CompleteOperation(
        Entity<SaboteurComponent?> ent,
        Entity<SaboteurRuleComponent?> rule,
        ProtoId<SaboteurOperationPrototype> operationId,
        int? reputationGain = null,
        bool isMajor = false)
    {
        if (!Resolve(ent, ref ent.Comp, logMissing: false))
            return;

        if (!Resolve(rule, ref rule.Comp))
            return;

        // Resolve the mind once and pass it through to avoid repeated mind-entity lookups.
        if (!TryGetMindComp(ent, out var mindId, out var mindComp, out var mind))
            return;

        if (mindComp.CompletedOperations.Contains(operationId))
        {
            Log.Warning($"Operation {operationId} already completed by {ToPrettyString(ent)}");
            return;
        }

        mindComp.CompletedOperations.Add(operationId);
        var gainedReputation = reputationGain ?? rule.Comp.DefaultOperationRepGain;

        AddReputation(ent, rule, gainedReputation, mindId, mindComp);

        var tcGain = SaboteurTierHelper.GetTelecrystalReward(rule.Comp.Tiers, mindComp.ReputationTier, isMajor);
        GrantTelecrystals(ent, rule, tcGain, mindComp);

        NotifyPlayer(ent, Loc.GetString("saboteur-operation-complete-detailed",
            ("rep", gainedReputation),
            ("tc", tcGain),
            ("totalRep", mindComp.Reputation),
            ("tier", SaboteurTierHelper.GetTierName(rule.Comp.Tiers, mindComp.ReputationTier))));

        var completedEv = new SaboteurOperationCompletedEvent(mindId, operationId, mindComp.ReputationTier, gainedReputation);
        RaiseLocalEvent(rule, ref completedEv);

        RemoveCompletedObjective(mindId, mind, operationId, ent);

        if (!mindComp.GloriousDeathActive)
            AssignNextObjective(ent, mindComp);
    }

    private void RemoveCompletedObjective(
        EntityUid mindId,
        MindComponent mind,
        ProtoId<SaboteurOperationPrototype> operationId,
        EntityUid saboteur)
    {
        for (var i = mind.Objectives.Count - 1; i >= 0; i--)
        {
            var objective = mind.Objectives[i];
            if (!TryComp<SaboteurOperationComponent>(objective, out var comp))
                continue;

            if (comp.OperationId == operationId)
            {
                _mind.TryRemoveObjective(mindId, mind, i);
                Log.Debug($"Removed completed objective {operationId} from {ToPrettyString(saboteur)}");
                return;
            }
        }
    }

    /// <summary>
    /// Adds reputation to a saboteur, applying the exposure multiplier, and triggers
    /// tier advancement if thresholds are crossed.
    /// </summary>
    public void AddReputation(Entity<SaboteurComponent?> ent, Entity<SaboteurRuleComponent?> rule, int amount)
    {
        if (!Resolve(ent, ref ent.Comp, logMissing: false))
            return;

        if (!Resolve(rule, ref rule.Comp))
            return;

        if (!TryGetMindComp(ent, out var mindId, out var mindComp))
            return;

        AddReputation(ent, rule, amount, mindId, mindComp);
    }

    /// <summary>
    /// Internal overload that skips the mind lookup when the caller has already resolved it.
    /// </summary>
    /// <remarks>Caller must have already resolved ent.Comp and rule.Comp.</remarks>
    private void AddReputation(
        Entity<SaboteurComponent?> ent,
        Entity<SaboteurRuleComponent?> rule,
        int amount,
        EntityUid mindId,
        SaboteurMindComponent mindComp)
    {
        var effectiveAmount = (int) Math.Round(amount * mindComp.ExposurePenaltyMultiplier);
        if (SaboteurTierHelper.IsExposed(mindComp.ExposurePenaltyMultiplier))
        {
            Log.Debug($"Saboteur {ToPrettyString(ent)} rep reduced by exposure " +
                      $"({mindComp.HighestObservedStatus}, ×{mindComp.ExposurePenaltyMultiplier}): {amount} → {effectiveAmount}");
        }

        var oldReputation = mindComp.Reputation;
        mindComp.Reputation += effectiveAmount;

        Log.Debug($"Saboteur {ToPrettyString(ent)} gained {effectiveAmount} rep " +
                  $"({oldReputation} → {mindComp.Reputation})");

        var repEv = new SaboteurReputationChangedEvent(mindId, oldReputation, mindComp.Reputation);
        RaiseLocalEvent(rule.Owner, ref repEv);

        var newTier = SaboteurTierHelper.GetTierFromReputation(rule.Comp!.Tiers, mindComp.Reputation);
        if (newTier > mindComp.ReputationTier)
            ProgressTier(ent, newTier, (rule.Owner, rule.Comp));
    }

    private void ProgressTier(EntityUid saboteur, int newTier, Entity<SaboteurRuleComponent> rule)
    {
        if (!TryGetMindComp(saboteur, out var mindId, out var mindComp))
            return;

        var oldTier = mindComp.ReputationTier;
        mindComp.ReputationTier = newTier;

        var tierEv = new SaboteurTierAdvancedEvent(mindId, oldTier, newTier);
        RaiseLocalEvent(rule.Owner, ref tierEv);

        if (newTier >= 1 && newTier <= SaboteurTierHelper.GetMaxTier(rule.Comp.Tiers))
        {
            Log.Info($"Saboteur {ToPrettyString(saboteur)} unlocked tier {newTier} operations");

            // Only assign from the new tier if there is a free slot.
            if (CountActiveSaboteurObjectives(saboteur) < rule.Comp.MaxActiveObjectives)
                AssignObjectiveFromTier(saboteur, rule.Comp, newTier);
        }

        var tierName = SaboteurTierHelper.GetTierName(rule.Comp.Tiers, newTier);
        var nextTierRep = SaboteurTierHelper.GetNextTierThreshold(rule.Comp.Tiers, newTier);

        if (nextTierRep > 0)
        {
            NotifyPlayer(saboteur, Loc.GetString("saboteur-tier-up-detailed",
                ("tier", newTier),
                ("tierName", tierName),
                ("nextRep", nextTierRep)));
        }
        else
        {
            NotifyPlayer(saboteur, Loc.GetString("saboteur-tier-up-max",
                ("tier", newTier),
                ("tierName", tierName)));
        }
    }

    /// <summary>
    /// Grants telecrystals to a saboteur's uplink.
    /// </summary>
    public void GrantTelecrystals(Entity<SaboteurComponent?> ent, Entity<SaboteurRuleComponent?> rule, int amount)
    {
        if (amount <= 0)
            return;

        if (!Resolve(ent, ref ent.Comp, logMissing: false))
            return;

        if (!Resolve(rule, ref rule.Comp))
            return;

        if (!TryGetMindComp(ent, out _, out var mindComp))
            return;

        GrantTelecrystals(ent, rule, amount, mindComp);
    }

    /// <summary>
    /// Internal overload that skips the mind lookup when the caller has already resolved it.
    /// </summary>
    /// <remarks>Caller must have already resolved ent.Comp and rule.Comp.</remarks>
    private void GrantTelecrystals(
        Entity<SaboteurComponent?> ent,
        Entity<SaboteurRuleComponent?> rule,
        int amount,
        SaboteurMindComponent mindComp)
    {
        if (amount <= 0)
            return;

        if (mindComp.UplinkEntity is not { } uplinkEntity)
        {
            Log.Warning($"Saboteur {ToPrettyString(ent)} has no uplink to grant {amount} TC to");
            return;
        }

        var currency = new Dictionary<string, FixedPoint2>(1) { [rule.Comp!.TelecrystalCurrency] = amount };
        if (!_store.TryAddCurrency(currency, uplinkEntity))
        {
            Log.Warning($"Failed to add {amount} TC to uplink for saboteur {ToPrettyString(ent)}");
            return;
        }

        Log.Debug($"Saboteur {ToPrettyString(ent)} granted {amount} TC");
    }

    private bool AssignObjectiveFromTier(EntityUid saboteur, SaboteurRuleComponent rule, int tier)
    {
        if (!_mind.TryGetMind(saboteur, out var mindId, out var mind))
            return false;

        var objectives = GetTierObjectives(tier);
        if (objectives == null || objectives.Count == 0)
        {
            Log.Debug($"No objectives in tier-cache for tier {tier}");
            return false;
        }

        var shuffled = _scratchObjectivePool;
        shuffled.Clear();
        shuffled.AddRange(objectives);
        _random.Shuffle(shuffled);

        foreach (var proto in shuffled)
        {
            var created = _objectives.TryCreateObjective(mindId, mind, proto);
            if (created == null)
            {
                Log.Debug($"TryCreateObjective failed for {proto} (tier {tier}) for {ToPrettyString(saboteur)}");
                continue;
            }

            _mind.AddObjective(mindId, mind, created.Value);
            Log.Info($"Assigned {ToPrettyString(created.Value)} from tier {tier} to {ToPrettyString(saboteur)}");
            return true;
        }

        Log.Debug($"No valid objective from tier {tier} for {ToPrettyString(saboteur)} " +
                  $"(tried {shuffled.Count} prototypes, {mind.Objectives.Count} already assigned)");
        return false;
    }

    /// <summary>
    /// Collects all eligible (non-time-gated) tiers up to <paramref name="maxTier"/>,
    /// shuffles them, and tries to assign an objective from each in random order.
    /// Falls back through all tiers before giving up.
    /// </summary>
    private bool TryAssignFromRandomEligibleTier(EntityUid saboteur, SaboteurRuleComponent rule, int? maxTier = null)
    {
        var cap = maxTier ?? SaboteurTierHelper.GetMaxTier(rule.Tiers);

        _scratchEligibleTiers.Clear();
        for (var tier = 0; tier <= cap; tier++)
        {
            if (tier >= rule.HighTierMinimum && !IsHighTierTimeGateMet(rule))
                continue;

            _scratchEligibleTiers.Add(tier);
        }

        if (_scratchEligibleTiers.Count == 0)
            return false;

        _random.Shuffle(_scratchEligibleTiers);

        foreach (var tier in _scratchEligibleTiers)
        {
            if (AssignObjectiveFromTier(saboteur, rule, tier))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Counts how many active saboteur operation objectives this entity currently has.
    /// </summary>
    private int CountActiveSaboteurObjectives(EntityUid saboteur)
    {
        if (!_mind.TryGetMind(saboteur, out _, out var mind))
            return 0;

        var count = 0;
        foreach (var objective in mind.Objectives)
        {
            if (HasComp<SaboteurOperationComponent>(objective))
                count++;
        }

        return count;
    }

    private bool IsHighTierTimeGateMet(SaboteurRuleComponent rule)
    {
        return _gameTicker.RoundDuration() >= rule.HighTierTimeGate;
    }

    /// <summary>
    /// Assigns initial objectives to a newly created saboteur,
    /// filling up to <see cref="SaboteurRuleComponent.MaxActiveObjectives"/> slots.
    /// Tries all eligible non-time-gated tiers in random order per slot, falling back
    /// to the weighted traitor objective pool if saboteur-specific objectives are exhausted.
    /// </summary>
    public void AssignInitialObjective(Entity<SaboteurComponent?> ent, Entity<SaboteurRuleComponent?> rule)
    {
        if (!Resolve(ent, ref ent.Comp, logMissing: false))
            return;

        if (!Resolve(rule, ref rule.Comp))
            return;

        var current = CountActiveSaboteurObjectives(ent);
        if (current >= rule.Comp.MaxActiveObjectives)
        {
            Log.Debug($"Saboteur {ToPrettyString(ent)} already has {current} objectives, skipping initial assignment");
            return;
        }

        var target = rule.Comp.MaxActiveObjectives;
        var assigned = 0;

        for (var i = current; i < target; i++)
        {
            // Try saboteur-specific objectives from all eligible tiers (random order).
            if (TryAssignFromRandomEligibleTier(ent, rule.Comp))
            {
                assigned++;
                continue;
            }

            // Fall back to the weighted traitor objective pool.
            if (!string.IsNullOrEmpty(rule.Comp.TraitorFallbackGroup)
                && AssignObjectiveFromWeightedGroup(ent, rule.Comp.TraitorFallbackGroup))
            {
                assigned++;
                continue;
            }

            Log.Warning($"Saboteur {ToPrettyString(ent)} could not fill objective slot {i + 1}/{target} " +
                         $"(assigned {assigned} so far, {CountActiveSaboteurObjectives(ent)} active)");
        }

        if (assigned == 0)
            Log.Warning($"Could not assign any initial objectives to saboteur {ToPrettyString(ent)}");
        else if (assigned < target - current)
            Log.Warning($"Saboteur {ToPrettyString(ent)} assigned only {assigned}/{target - current} initial objectives");
        else
            Log.Info($"Saboteur {ToPrettyString(ent)} assigned {assigned} initial objectives");
    }

    /// <summary>
    /// Fills empty objective slots up to <see cref="SaboteurRuleComponent.MaxActiveObjectives"/>.
    /// Randomly picks from eligible tiers up to the saboteur's current reputation tier.
    /// </summary>
    private void AssignNextObjective(EntityUid saboteur, SaboteurMindComponent comp)
    {
        if (!_conditionCore.TryGetRule(out var rule))
            return;

        var slotsToFill = rule.MaxActiveObjectives - CountActiveSaboteurObjectives(saboteur);

        for (var slot = 0; slot < slotsToFill; slot++)
        {
            if (TryAssignFromRandomEligibleTier(saboteur, rule, comp.ReputationTier))
                continue;

            if (!string.IsNullOrEmpty(rule.TraitorFallbackGroup)
                && AssignObjectiveFromWeightedGroup(saboteur, rule.TraitorFallbackGroup))
            {
                continue;
            }

            // No more objectives available; assign DAGD and stop filling.
            AssignDieAGloriousDeath(saboteur);
            return;
        }
    }

    private bool AssignObjectiveFromWeightedGroup(EntityUid saboteur, string groupId)
    {
        if (!_mind.TryGetMind(saboteur, out var mindId, out var mind))
            return false;

        var objective = _objectives.GetRandomObjective(mindId, mind, groupId, float.MaxValue);
        if (objective == null)
            return false;

        _mind.AddObjective(mindId, mind, objective.Value);
        Log.Info($"Assigned fallback objective {ToPrettyString(objective.Value)} from {groupId} to {ToPrettyString(saboteur)}");
        return true;
    }

    private void AssignDieAGloriousDeath(EntityUid saboteur)
    {
        if (!_mind.TryGetMind(saboteur, out var mindId, out var mind))
            return;

        if (!_conditionCore.TryGetRule(out var rule))
            return;

        var objective = _objectives.TryCreateObjective(mindId, mind, rule.GloriousDeathObjectiveId);
        if (objective == null)
        {
            Log.Error($"Failed to create DAGD objective for {ToPrettyString(saboteur)}");
            NotifyPlayer(saboteur, Loc.GetString("saboteur-no-objectives-remaining"));
            return;
        }

        _mind.AddObjective(mindId, mind, objective.Value);
        NotifyPlayer(saboteur, Loc.GetString("saboteur-glorious-death-assigned"));
        Log.Info($"Saboteur {ToPrettyString(saboteur)} assigned DAGD (all pools exhausted)");

        if (TryGetMindComp(saboteur, out _, out var mindComp))
            mindComp.GloriousDeathActive = true;
    }

    /// <summary>
    /// Clears the Glorious Death flag and attempts to assign a new objective.
    /// </summary>
    public void ResetGloriousDeath(Entity<SaboteurComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, logMissing: false))
            return;

        if (!TryGetMindComp(ent, out _, out var mindComp))
            return;

        if (!mindComp.GloriousDeathActive)
            return;

        mindComp.GloriousDeathActive = false;
        Log.Info($"Saboteur {ToPrettyString(ent)} DAGD flag reset; new objectives can be assigned.");

        AssignNextObjective(ent, mindComp);
    }

    private bool TryGetCriminalStatus(EntityUid saboteur, out SecurityStatus status)
    {
        status = SecurityStatus.None;

        if (!TryGetMindComp(saboteur, out _, out var mindComp))
            return false;

        if (mindComp.OriginalRecordKey is not { } key)
            return false;

        if (!_stationRecords.TryGetRecord<CriminalRecord>(key, out var criminal))
            return false;

        status = criminal.Status;
        return true;
    }

    private void NotifyPlayer(EntityUid saboteur, string message)
    {
        if (!TryComp<ActorComponent>(saboteur, out var actor))
            return;

        var wrappedMessage = Loc.GetString("chat-manager-server-wrap-message", ("message", message));
        _chatManager.ChatMessageToOne(ChatChannel.Server, message, wrappedMessage, default, false, actor.PlayerSession.Channel);
    }

    /// <summary>
    /// Returns the list of objective prototype IDs available at the given tier,
    /// or null if no objectives exist for that tier.
    /// </summary>
    public List<EntProtoId>? GetTierObjectives(int tier)
    {
        return _tierObjectivesIndex.TryGetValue(tier, out var list) ? list : null;
    }

    /// <summary>
    /// Rebuilds the system-level tier → objectives index by scanning all
    /// entity prototypes that have <see cref="SaboteurOperationComponent"/>.
    /// Called once at system init and again whenever prototypes are reloaded.
    /// </summary>
    private void RebuildTierObjectivesIndex()
    {
        _tierObjectivesIndex.Clear();

        foreach (var proto in _prototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract)
                continue;

            if (!proto.TryGetComponent<SaboteurOperationComponent>(out var opComp, _componentFactory))
                continue;

            var tier = opComp.Tier;
            if (!_tierObjectivesIndex.TryGetValue(tier, out var list))
                _tierObjectivesIndex[tier] = list = new List<EntProtoId>();

            list.Add(proto.ID);
        }

        var sb = new StringBuilder("[SaboteurOps] Tier objectives index: ");
        var first = true;
        foreach (var (tier, protos) in _tierObjectivesIndex)
        {
            if (!first) sb.Append(", ");
            sb.Append($"tier{tier}={protos.Count}");
            first = false;
        }
        Log.Info(sb.ToString());
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            RebuildTierObjectivesIndex();
    }

    private static int GetSeverity(SecurityStatus status)
    {
        return SecuritySeverity.GetValueOrDefault(status, 0);
    }
}
