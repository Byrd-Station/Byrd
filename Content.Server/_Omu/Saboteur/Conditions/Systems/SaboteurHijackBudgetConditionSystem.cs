// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Robust.Shared.Prototypes;

using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates budget-hijack objectives by comparing current cargo account balances
/// against the snapshot taken at assignment time.
/// </summary>
public sealed class SaboteurHijackBudgetConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurHijackBudgetConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurHijackBudgetConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<SaboteurHijackBudgetConditionComponent, RequirementCheckEvent>(OnRequirementCheck);
    }

    /// <summary>
    /// Resolves the account name that should be excluded from budget checks,
    /// using the saboteur's starting department and the component's override map.
    /// </summary>
    private ProtoId<CargoAccountPrototype>? ResolveExcludedAccount(
        SaboteurHijackBudgetConditionComponent comp,
        EntityUid mindOwner)
    {
        if (!TryComp<SaboteurMindComponent>(mindOwner, out var mindComp)
            || mindComp.StartingDepartment is not { } dept)
            return null;

        if (comp.DepartmentOverrideMap.TryGetValue(dept, out var mapped))
            return mapped;

        // Most departments share their name with the corresponding cargo account
        // (e.g. "Engineering" department → "Engineering" cargo account).
        return new ProtoId<CargoAccountPrototype>(dept.Id);
    }

    private void OnGetProgress(EntityUid uid, SaboteurHijackBudgetConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out var saboteur, out var dirty))
            return;

        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        var excludeAccount = ResolveExcludedAccount(comp, args.Mind.Owner);

        foreach (var station in _station.GetStations())
        {
            if (!TryComp<StationBankAccountComponent>(station, out var bank))
                continue;

            foreach (var (account, balance) in bank.Accounts)
            {
                if (excludeAccount != null && account == excludeAccount)
                    continue;

                if (!comp.BudgetAtAssignment.TryGetValue(account, out var atAssignment) || atAssignment <= 0)
                    continue;

                if (balance <= 0)
                {
                    args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 1f);
                    return;
                }
            }
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
    }

    private void OnAfterAssign(EntityUid uid, SaboteurHijackBudgetConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        comp.CacheKey = _core.MakeCacheKey(uid);
        var excludeAccount = ResolveExcludedAccount(comp, args.Mind.Owner);

        foreach (var station in _station.GetStations())
        {
            if (!TryComp<StationBankAccountComponent>(station, out var bank))
                continue;

            foreach (var (account, balance) in bank.Accounts)
            {
                if (excludeAccount != null && account == excludeAccount)
                    continue;

                comp.BudgetAtAssignment[account] = balance;
            }
        }

        _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.Bank);
    }

    private void OnRequirementCheck(EntityUid uid, SaboteurHijackBudgetConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        var excludeAccount = ResolveExcludedAccount(comp, args.Mind.Owner);

        foreach (var station in _station.GetStations())
        {
            if (!TryComp<StationBankAccountComponent>(station, out var bank))
                continue;

            foreach (var (account, balance) in bank.Accounts)
            {
                if (excludeAccount != null && account == excludeAccount)
                    continue;

                if (balance > 0)
                    return;
            }
        }

        args.Cancelled = true;
    }
}
