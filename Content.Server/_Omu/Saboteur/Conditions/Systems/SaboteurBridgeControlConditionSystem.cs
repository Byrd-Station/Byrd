// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server.Communications;
using Content.Server.Pinpointer;
using Content.Shared.Access.Components;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;

using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;
using Content.Server._Omu.Saboteur;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates bridge-control objectives by tracking station announcements made by the saboteur.
/// </summary>
public sealed class SaboteurBridgeControlConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SaboteurDepartmentConfigSystem _deptConfig = default!;
    [Dependency] private readonly NavMapSystem _navMap = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurTakeBridgeControlConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurTakeBridgeControlConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<SaboteurTakeBridgeControlConditionComponent, RequirementCheckEvent>(OnRequirementCheck);

        SubscribeLocalEvent<SaboteurAnnouncementMadeEvent>(OnAnnouncementMade);
    }

    private void OnGetProgress(EntityUid uid, SaboteurTakeBridgeControlConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out _, out var dirty))
            return;

        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey,
            _core.CountProgress(comp.AnnouncementCount, comp.RequiredCount));
    }

    private void OnAfterAssign(EntityUid uid, SaboteurTakeBridgeControlConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        comp.CacheKey = _core.MakeCacheKey(uid);
        _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.Announcement);
    }

    private void OnRequirementCheck(EntityUid uid, SaboteurTakeBridgeControlConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_deptConfig.TryGetDeptConfig(out var dept))
        {
            args.Cancelled = true;
            return;
        }

        var query = EntityQueryEnumerator<CommunicationsConsoleComponent, AccessReaderComponent>();
        while (query.MoveNext(out var consoleUid, out _, out var reader))
        {
            if (HasCommandAccess(dept.CommandAccessTag, reader) && IsConsoleNearBridge(dept.BridgeBeaconKeyword, consoleUid))
                return;
        }

        args.Cancelled = true;
    }

    public bool IsConsoleNearBridge(string bridgeBeaconKeyword, EntityUid consoleUid)
    {
        if (!_navMap.TryGetNearestBeacon(consoleUid, out var beacon, out _))
            return false;

        var beaconText = beacon.Value.Comp.Text;
        if (string.IsNullOrEmpty(beaconText))
            return false;

        return beaconText.Contains(bridgeBeaconKeyword, StringComparison.OrdinalIgnoreCase);
    }

    private void OnAnnouncementMade(ref SaboteurAnnouncementMadeEvent args)
    {
        if (!_deptConfig.TryGetDeptConfig(out var dept))
            return;

        if (!TryComp<AccessReaderComponent>(args.ConsoleUid, out var reader) || !HasCommandAccess(dept.CommandAccessTag, reader))
            return;

        if (!IsConsoleNearBridge(dept.BridgeBeaconKeyword, args.ConsoleUid))
            return;

        foreach (var objUid in args.Mind.Objectives)
        {
            if (TryComp<SaboteurTakeBridgeControlConditionComponent>(objUid, out var bridgeComp))
            {
                bridgeComp.AnnouncementCount++;
                break;
            }
        }
    }

    /// <summary>
    /// Checks whether the given access reader has command-level access.
    /// </summary>
    public bool HasCommandAccess(string commandAccessTag, AccessReaderComponent reader)
    {
        foreach (var accessSet in reader.AccessLists)
        {
            if (accessSet.Contains(commandAccessTag))
                return true;
        }
        return false;
    }
}
