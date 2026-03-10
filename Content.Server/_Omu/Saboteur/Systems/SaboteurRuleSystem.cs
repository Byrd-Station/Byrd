// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Content.Server.Antag;
using Content.Server.Codewords;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.Roles;
using Content.Server.Roles.RoleCodeword;
using Content.Server.Traitor.Uplink;
using Content.Goobstation.Common.Traitor;
using Content.Goobstation.Shared.ManifestListings;
using Content.Shared._Omu.Saboteur;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.RoleCodeword;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Implants.Components;
using Content.Shared.StationRecords;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Shared.Prototypes;

using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Systems;

/// <summary>
/// Game rule system for the saboteur antagonist. Handles antag selection,
/// uplink setup, tier advancement, late-join catch-up, and round-end summaries.
/// </summary>
public sealed class SaboteurRuleSystem : GameRuleSystem<SaboteurRuleComponent>
{
    /// <summary>
    /// Prototype ID for the saboteur mind role entity.
    /// </summary>
    private static readonly EntProtoId SaboteurMindRoleId = "MindRoleSaboteur";

    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly UplinkSystem _uplink = default!;
    [Dependency] private readonly GoobCommonUplinkSystem _goobUplink = default!;
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly SaboteurOperationSystem _saboteurOps = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly SharedRoleSystem _roleSystem = default!;
    [Dependency] private readonly CodewordSystem _codewordSystem = default!;
    [Dependency] private readonly RoleCodewordSystem _roleCodewordSystem = default!;
    [Dependency] private readonly SaboteurDepartmentConfigSystem _deptConfig = default!;
    [Dependency] private readonly SaboteurConditionCoreSystem _conditionCore = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SaboteurRuleComponent, AfterAntagEntitySelectedEvent>(OnAfterAntagEntitySelected);
        SubscribeLocalEvent<SaboteurRuleComponent, ObjectivesTextPrependEvent>(OnObjectivesTextPrepend);
        SubscribeLocalEvent<SaboteurMindComponent, PrependObjectivesSummaryTextEvent>(OnPrependObjectivesSummary);
        SubscribeLocalEvent<SaboteurRuleComponent, SaboteurTierAdvancedEvent>(OnTierAdvanced);
    }

    protected override void ActiveTick(EntityUid uid, SaboteurRuleComponent comp, GameRuleComponent gameRule, float frameTime)
    {
        if (!_conditionCore.TryGetDirtyTracking(out var dirty))
            return;

        if (dirty.CompletionSweepNeeded)
        {
            dirty.CompletionSweepNeeded = false;
            _saboteurOps.RunCompletionSweep((uid, comp));
        }
    }

    private void OnAfterAntagEntitySelected(Entity<SaboteurRuleComponent> ruleEnt, ref AfterAntagEntitySelectedEvent args)
    {
        if (HasComp<SaboteurComponent>(args.EntityUid))
        {
            Log.Warning($"Entity {ToPrettyString(args.EntityUid)} is already a saboteur, skipping duplicate assignment.");
            return;
        }

        MakeSaboteur((args.EntityUid, null), ruleEnt.Owner);
    }

    private void OnTierAdvanced(Entity<SaboteurRuleComponent> ent, ref SaboteurTierAdvancedEvent args)
    {
        if (!TryComp<SaboteurMindComponent>(args.Mind, out var mindComp))
            return;

        UnlockStoreCategoriesForTier(mindComp, args.NewTier, ent.Comp);
    }

    private void UnlockStoreCategoriesForTier(SaboteurMindComponent mindComp, int newTier, SaboteurRuleComponent rule)
    {
        if (mindComp.UplinkEntity is not { } uplinkEntity)
            return;

        if (!TryComp<StoreComponent>(uplinkEntity, out var store))
            return;

        foreach (var (category, requiredTier) in rule.CategoryTierMap)
        {
            if (requiredTier == newTier && !store.Categories.Contains(category))
                store.Categories.Add(category);
        }
    }

    private void OnObjectivesTextPrepend(Entity<SaboteurRuleComponent> ent, ref ObjectivesTextPrependEvent args)
    {
        var codewords = _codewordSystem.GetCodewords(ent.Comp.CodewordFaction);
        var sb = new StringBuilder(args.Text);
        sb.AppendLine();
        sb.AppendLine(Loc.GetString("saboteur-round-end-header"));
        sb.Append(Loc.GetString("saboteur-round-end-codewords", ("codewords", string.Join(", ", codewords))));
        args.Text = sb.ToString();
    }

    private void OnPrependObjectivesSummary(Entity<SaboteurMindComponent> ent, ref PrependObjectivesSummaryTextEvent args)
    {
        var mindComp = ent.Comp;

        if (!_conditionCore.TryGetRule(out var rule))
            return;

        var sb = new StringBuilder();
        var tierName = SaboteurTierHelper.GetTierName(rule.Tiers, mindComp.ReputationTier);
        sb.AppendLine(Loc.GetString("saboteur-round-end-stats",
            ("tier", mindComp.ReputationTier),
            ("tierName", tierName),
            ("rep", mindComp.Reputation),
            ("ops", mindComp.CompletedOperations.Count)));

        if (SaboteurTierHelper.IsExposed(mindComp.ExposurePenaltyMultiplier))
        {
            var penaltyPercent = (int) ((1f - mindComp.ExposurePenaltyMultiplier) * 100);
            sb.AppendLine(Loc.GetString("saboteur-round-end-exposed",
                ("status", Loc.GetString(SaboteurTierHelper.GetSecurityStatusLocKey(mindComp.HighestObservedStatus))),
                ("penalty", penaltyPercent)));
        }
        else
        {
            sb.AppendLine(Loc.GetString("saboteur-round-end-clean"));
        }

        args.Text += sb.ToString();
    }

    /// <summary>
    /// Fully initialises the given entity as a saboteur: adds components, uplink,
    /// codewords, catch-up bonuses, department detection, and the first objective.
    /// </summary>
    /// <remarks>
    /// Initialization order and failure modes:
    /// 1. <see cref="EnsureComp{T}(EntityUid)"/> for <see cref="SaboteurComponent"/> — always succeeds.
    /// 2. Mind lookup — logs warning and returns if no mind is found.
    ///    If this fails the entity will have a <see cref="SaboteurComponent"/> with no mind linkage;
    ///    callers should handle this edge case.
    /// 3. <see cref="EnsureComp{T}(EntityUid)"/> for <see cref="SaboteurMindComponent"/> — always succeeds.
    /// 4. Role assignment.
    /// 5. Uplink setup — logs warning if it fails but continues.
    /// 6. Late-join catch-up.
    /// 7. Department detection.
    /// 8. Codewords / briefing.
    /// 9. Initial objective assignment.
    /// </remarks>
    public void MakeSaboteur(Entity<SaboteurComponent?> ent, Entity<SaboteurRuleComponent?> rule)
    {
        if (!Resolve(rule, ref rule.Comp))
            return;

        EnsureComp<SaboteurComponent>(ent);

        if (!_mindSystem.TryGetMind(ent, out var mindId, out var mind))
        {
            Log.Warning($"Could not get mind for saboteur {ToPrettyString(ent)}");
            return;
        }

        var mindComp = EnsureComp<SaboteurMindComponent>(mindId);
        mindComp.ReputationTier = 0;
        mindComp.Reputation = 0;

        // Assign the mind role so the player is recognized as an antagonist
        // and so downstream code (briefings, round-end) can find SaboteurRoleComponent.
        if (!_roleSystem.MindHasRole<SaboteurRoleComponent>(mindId))
            _roleSystem.MindAddRole(mindId, SaboteurMindRoleId, mind, silent: true);

        var setupEvent = SetupUplink(ent, mindId, mindComp, rule.Comp);

        ApplyLateJoinCatchUp(ent, mindComp, rule);

        mindComp.StartingDepartment = GetDepartmentForEntity(ent);

        CaptureOriginalRecordKey(ent, mindComp);

        SetupCodewordsAndBriefing(ent, mindId, rule.Comp, setupEvent);

        _saboteurOps.AssignInitialObjective(ent, rule);

        Log.Info($"Saboteur {ToPrettyString(ent)} initialized (dept: {mindComp.StartingDepartment?.Id ?? "unknown"})");
    }

    private SetupUplinkEvent? SetupUplink(EntityUid saboteur, EntityUid mindId, SaboteurMindComponent mindComp, SaboteurRuleComponent rule)
    {
        var uplinkPreference = _goobUplink.GetUplinkPreference(mindId);
        if (!_uplink.TryAddUplink(saboteur, rule.StartingTc, uplinkPreference, out var uplinkEntity, out var setupEvent))
        {
            Log.Warning($"Failed to add uplink to saboteur {ToPrettyString(saboteur)}");
            return setupEvent;
        }

        uplinkEntity ??= FindImplantUplink(saboteur);

        if (uplinkEntity != null)
        {
            mindComp.UplinkEntity = uplinkEntity;
            ConfigureSaboteurStore(uplinkEntity.Value, rule);
        }
        else
        {
            Log.Warning($"Saboteur {ToPrettyString(saboteur)} uplink created but entity not found");
        }

        return setupEvent;
    }

    private void SetupCodewordsAndBriefing(EntityUid saboteur, EntityUid mindId, SaboteurRuleComponent rule, SetupUplinkEvent? setupEvent)
    {
        var codewords = _codewordSystem.GetCodewords(rule.CodewordFaction);
        var codewordComp = EnsureComp<RoleCodewordComponent>(mindId);
        _roleCodewordSystem.SetRoleCodewords((mindId, codewordComp), "saboteur", new List<string>(codewords), rule.CodewordColor);

        // Send the full colored briefing to chat
        _antag.SendBriefing(saboteur, GenerateBriefingChat(codewords, setupEvent), null, null);

        // Set the plain-text briefing for the character info menu
        _roleSystem.MindHasRole<SaboteurRoleComponent>(mindId, out var saboteurRole);
        if (saboteurRole is not null)
        {
            EnsureComp<RoleBriefingComponent>(saboteurRole.Value.Owner, out var briefingComp);
            briefingComp.Briefing = GenerateBriefingCharacter(codewords, setupEvent);
        }
    }

    private void ApplyLateJoinCatchUp(EntityUid saboteur, SaboteurMindComponent comp, Entity<SaboteurRuleComponent?> rule)
    {
        if (!Resolve(rule, ref rule.Comp))
            return;

        var roundDuration = GameTicker.RoundDuration();
        var minutesElapsed = roundDuration.TotalMinutes;

        if (minutesElapsed < rule.Comp.LateJoinMinMinutes)
            return;

        var catchUpRep = (int) Math.Min(minutesElapsed * rule.Comp.LateJoinRepPerMinute, rule.Comp.MaxLateJoinRep);
        if (catchUpRep > 0)
        {
            _saboteurOps.AddReputation(saboteur, rule, catchUpRep);
            Log.Info($"Late-join saboteur {ToPrettyString(saboteur)} received {catchUpRep} catch-up rep ({minutesElapsed:F0}m)");
        }

        if (rule.Comp.LateJoinTcIntervalMinutes <= 0)
            return;

        var completedIntervals = (int) (minutesElapsed / rule.Comp.LateJoinTcIntervalMinutes);
        var catchUpTC = completedIntervals * rule.Comp.LateJoinTcPerInterval;
        if (catchUpTC > 0)
        {
            _saboteurOps.GrantTelecrystals(saboteur, rule, catchUpTC);
            Log.Info($"Late-join saboteur {ToPrettyString(saboteur)} received {catchUpTC} catch-up TC");
        }
    }

    /// <summary>
    /// Builds the full colored briefing sent to the player's chat on role assignment.
    /// </summary>
    private string GenerateBriefingChat(string[] codewords, SetupUplinkEvent? setupEvent)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("saboteur-role-greeting"));
        sb.AppendLine(Loc.GetString("saboteur-briefing-codewords", ("codewords", string.Join(", ", codewords))));
        sb.AppendLine(Loc.GetString("saboteur-briefing-stealth-warning"));
        sb.AppendLine(Loc.GetString("saboteur-briefing-tiers"));

        if (setupEvent != null)
            sb.AppendLine(setupEvent.Value.BriefingEntry);
        else
            sb.AppendLine(Loc.GetString("saboteur-role-uplink-implant"));

        return sb.ToString();
    }

    private void ConfigureSaboteurStore(EntityUid uplinkEntity, SaboteurRuleComponent rule, int currentTier = 0)
    {
        if (!TryComp<StoreComponent>(uplinkEntity, out var store))
            return;

        // Clear pre-existing categories (e.g. from PDA prototypes) so only
        // tier-unlocked saboteur categories are present. The upstream purchase
        // handler rejects listings whose category isn't in this set.
        store.Categories.Clear();

        foreach (var category in rule.StoreCategories)
        {
            if (rule.CategoryTierMap.TryGetValue(category, out var requiredTier) && requiredTier > currentTier)
                continue;

            store.Categories.Add(category);
        }

        store.CurrencyWhitelist.Add(rule.TelecrystalCurrency);
    }

    private EntityUid? FindImplantUplink(EntityUid entity)
    {
        if (!TryComp<ImplantedComponent>(entity, out var implanted))
            return null;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (HasComp<StoreComponent>(implant))
                return implant;
        }

        return null;
    }

    /// <summary>
    /// Builds the plain-text briefing shown in the character info menu.
    /// No rich-text markup — the panel renders via AddText, not TryAddMarkup.
    /// </summary>
    private string GenerateBriefingCharacter(string[] codewords, SetupUplinkEvent? setupEvent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n" + Loc.GetString("saboteur-briefing-intro-short"));
        sb.AppendLine(Loc.GetString("saboteur-briefing-codewords-short", ("codewords", string.Join(", ", codewords))));
        sb.AppendLine(Loc.GetString("saboteur-briefing-stealth-warning-short"));
        sb.AppendLine(Loc.GetString("saboteur-briefing-tiers-short"));

        if (setupEvent != null && setupEvent.Value.BriefingEntryShort is { } shortEntry)
            sb.AppendLine(shortEntry);
        else
            sb.AppendLine(Loc.GetString("saboteur-briefing-uplink-implant-short"));

        return sb.ToString();
    }

    /// <summary>
    /// Captures the saboteur's station record key at assignment time so that
    /// exposure tracking always queries their original criminal record,
    /// regardless of later identity changes (ID swaps, name changes).
    /// </summary>
    private void CaptureOriginalRecordKey(EntityUid saboteur, SaboteurMindComponent mindComp)
    {
        if (!_idCard.TryFindIdCard(saboteur, out var idCard))
        {
            Log.Warning($"Saboteur {ToPrettyString(saboteur)} has no ID card — exposure tracking will be inactive.");
            return;
        }

        if (!TryComp<StationRecordKeyStorageComponent>(idCard, out var keyStorage)
            || keyStorage.Key is not { } key)
        {
            Log.Warning($"Saboteur {ToPrettyString(saboteur)} ID card has no station record key — exposure tracking will be inactive.");
            return;
        }

        mindComp.OriginalRecordKey = key;
    }

    private ProtoId<DepartmentPrototype>? GetDepartmentForEntity(EntityUid entity)
    {
        if (!_idCard.TryFindIdCard(entity, out var idCard))
            return null;

        var jobId = idCard.Comp.JobPrototype?.Id;
        if (string.IsNullOrEmpty(jobId))
            return null;

        if (!_deptConfig.TryGetDeptConfig(out var dept))
            return null;

        if (dept.JobToDepartment.TryGetValue(jobId, out var deptId))
            return deptId;

        return null;
    }
}
