// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server._Omu.Saboteur.Conditions;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.Radio.Components;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates map-threshold objectives by calculating what fraction of target entities
/// on the station have been sabotaged (powered down, disabled, bolted, etc.).
/// </summary>
public sealed class SaboteurMapThresholdConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SaboteurDepartmentConfigSystem _deptConfig = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private readonly Dictionary<string, Type?> _resolvedComponents = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurMapThresholdConditionComponent, ObjectiveGetProgressEvent>(OnMapThresholdGetProgress);
        SubscribeLocalEvent<SaboteurMapThresholdConditionComponent, ObjectiveAfterAssignEvent>(OnMapThresholdAfterAssign,
            after: new[] { typeof(SaboteurOperationSystem) });
        SubscribeLocalEvent<SaboteurMapThresholdConditionComponent, RequirementCheckEvent>(OnMapThresholdRequirementCheck);
    }

    private void OnMapThresholdGetProgress(EntityUid uid, SaboteurMapThresholdConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out _, out var dirty))
            return;

        if (comp.GroupByDepartment)
        {
            OnMapThresholdGroupedProgress(uid, comp, ref args, dirty);
            return;
        }

        var mode = comp.SabotageCheck;
        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        var total = comp.AssignedTargets.Count;
        if (total <= 0)
            return;

        int affected = 0;
        foreach (var target in comp.AssignedTargets)
        {
            if (!EntityManager.EntityExists(target) || _core.IsEntityDisabled(target, mode))
                affected++;
        }

        var primaryProgress = _core.CalculateThresholdProgress(affected, total, comp.Threshold);

        if (comp.SecondaryTargets.Count > 0)
        {
            int secTotal = comp.SecondaryTargets.Count;
            int secAffected = 0;
            foreach (var target in comp.SecondaryTargets)
            {
                if (!EntityManager.EntityExists(target) || _core.IsEntityDisabled(target, comp.SecondarySabotageCheck))
                    secAffected++;
            }

            var secondaryProgress = _core.CalculateThresholdProgress(secAffected, secTotal, comp.Threshold);
            var compositeResult = (primaryProgress + secondaryProgress) / 2f;
            args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, compositeResult);
            return;
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, primaryProgress);
    }

    private void OnMapThresholdGroupedProgress(EntityUid uid, SaboteurMapThresholdConditionComponent comp, ref ObjectiveGetProgressEvent args, SaboteurDirtyTrackingComponent dirty)
    {
        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        if (comp.DepartmentDoors.Count == 0)
            return;

        // Only evaluate the assigned target department
        if (comp.TargetDepartment is not { } targetDept
            || !comp.DepartmentDoors.TryGetValue(targetDept, out var targetDoors))
            return;

        int total = 0;
        int bolted = 0;
        foreach (var doorUid in targetDoors)
        {
            if (!EntityManager.EntityExists(doorUid))
                continue;

            total++;
            if (TryComp<DoorBoltComponent>(doorUid, out var bolt) && bolt.BoltsDown)
                bolted++;
        }

        if (total < comp.MinGroupCount)
            return;

        var fraction = (float) bolted / total;
        if (fraction >= comp.Threshold)
        {
            args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 1f);
            return;
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, fraction / comp.Threshold);
    }

    private void SnapshotDepartmentDoors(SaboteurMapThresholdConditionComponent comp, MapId mapId, Dictionary<string, ProtoId<DepartmentPrototype>> deptAccessTags)
    {
        comp.DepartmentDoors.Clear();

        var query = EntityQueryEnumerator<DoorBoltComponent, AccessReaderComponent, TransformComponent>();
        while (query.MoveNext(out var doorUid, out _, out var reader, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            foreach (var accessSet in reader.AccessLists)
            {
                foreach (var tag in accessSet)
                {
                    if (!deptAccessTags.TryGetValue(tag, out var dept))
                        continue;

                    if (!comp.DepartmentDoors.TryGetValue(dept, out var doorSet))
                        comp.DepartmentDoors[dept] = doorSet = new HashSet<EntityUid>();

                    doorSet.Add(doorUid);
                }
            }
        }
    }

    /// <summary>
    /// Selects an eligible target department (one with at least <see cref="SaboteurMapThresholdConditionComponent.MinGroupCount"/>
    /// doors) at random and updates the objective's name and description to include the department.
    /// </summary>
    private void PickTargetDepartment(EntityUid uid, SaboteurMapThresholdConditionComponent comp, MetaDataComponent meta)
    {
        var eligible = new List<ProtoId<DepartmentPrototype>>();
        foreach (var (dept, doors) in comp.DepartmentDoors)
        {
            if (doors.Count >= comp.MinGroupCount)
                eligible.Add(dept);
        }

        if (eligible.Count == 0)
        {
            Log.Warning($"Saboteur lockdown objective {ToPrettyString(uid)}: no departments with >= {comp.MinGroupCount} doors found");
            return;
        }

        comp.TargetDepartment = _random.Pick(eligible);

        // Update the objective name and description with the chosen department
        if (!TryComp<SaboteurOperationComponent>(uid, out var opComp))
            return;

        var proto = _prototypeManager.Index(opComp.OperationId);
        var ftlBase = proto.LocId;
        var deptName = Loc.GetString($"department-{comp.TargetDepartment}");
        _metaData.SetEntityName(uid, Loc.GetString(ftlBase, ("department", deptName)), meta);
        _metaData.SetEntityDescription(uid, Loc.GetString($"{ftlBase}-desc", ("department", deptName)), meta);
    }

    /// <remarks>
    /// Targets are frozen at objective assignment time. Entities spawned after assignment
    /// (new APCs, cameras placed by crew) are intentionally excluded — the objective
    /// represents the station's state at moment of assignment.
    /// </remarks>
    private void OnMapThresholdAfterAssign(EntityUid uid, SaboteurMapThresholdConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        comp.CacheKey = _core.MakeCacheKey(uid);

        if (comp.GroupByDepartment)
        {
            if (!_deptConfig.TryGetDeptConfig(out var deptConfig))
                return;

            if (_core.TryGetAnyStationMapId(out var groupedMapId))
                SnapshotDepartmentDoors(comp, groupedMapId, deptConfig.DepartmentAccessTags);

            PickTargetDepartment(uid, comp, args.Meta);

            _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.Bolts);
            return;
        }

        if (!_core.TryGetAnyStationMapId(out var mapId))
            return;

        SnapshotComponentEntities(comp.AssignedTargets, comp.TargetComponent, mapId);

        if (comp.SecondaryTargetComponent != null)
            SnapshotSecondaryEntities(comp.SecondaryTargets, comp.SecondaryTargetComponent, mapId, comp.SecondaryChannelFilter);

        var domains = _core.GetDomainsForCheckMode(comp.SabotageCheck);
        if (comp.SecondaryTargets.Count > 0)
            domains |= _core.GetDomainsForCheckMode(comp.SecondarySabotageCheck);

        _core.RegisterInterest(dirty, uid, domains);
    }

    private void OnMapThresholdRequirementCheck(EntityUid uid, SaboteurMapThresholdConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        if (comp.GroupByDepartment)
        {
            if (!_core.TryGetAnyStationMapId(out var mapId))
            {
                args.Cancelled = true;
                return;
            }

            if (!_deptConfig.TryGetDeptConfig(out var deptConfig))
            {
                args.Cancelled = true;
                return;
            }

            // Count doors per department and verify at least one department
            // meets MinGroupCount so the lockdown objective is completable.
            var deptDoorCounts = new Dictionary<ProtoId<DepartmentPrototype>, int>();
            var doorQuery = EntityQueryEnumerator<DoorBoltComponent, AccessReaderComponent, TransformComponent>();
            while (doorQuery.MoveNext(out _, out _, out var reader, out var xform))
            {
                if (xform.MapID != mapId)
                    continue;

                foreach (var accessSet in reader.AccessLists)
                {
                    foreach (var tag in accessSet)
                    {
                        if (!deptConfig.DepartmentAccessTags.TryGetValue(tag, out var dept))
                            continue;

                        deptDoorCounts[dept] = deptDoorCounts.GetValueOrDefault(dept) + 1;
                    }
                }
            }

            foreach (var count in deptDoorCounts.Values)
            {
                if (count >= comp.MinGroupCount)
                    return;
            }

            args.Cancelled = true;
            return;
        }

        if (!TryResolveComponentType(comp.TargetComponent, out var compType))
        {
            args.Cancelled = true;
            return;
        }

        if (!_core.TryGetAnyStationMapId(out var flatMapId))
        {
            args.Cancelled = true;
            return;
        }

        var xformQuery = GetEntityQuery<TransformComponent>();
        foreach (var (entUid, _) in EntityManager.GetAllComponents(compType))
        {
            if (xformQuery.GetComponent(entUid).MapID == flatMapId)
                return;
        }

        args.Cancelled = true;
    }

    private bool TryResolveComponentType(string componentName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Type? type)
    {
        if (_resolvedComponents.TryGetValue(componentName, out type))
            return type != null;

        if (_compFactory.TryGetRegistration(componentName, out var registration))
        {
            type = registration.Type;
            _resolvedComponents[componentName] = type;
            return true;
        }

        Log.Error($"Saboteur condition: unknown component '{componentName}' — " +
                  "this should have been caught by ComponentNameSerializer at load time. " +
                  "The objective will be non-functional for this round.");
        DebugTools.Assert(false, $"Unresolvable component name '{componentName}' in saboteur condition");
        _resolvedComponents[componentName] = null;
        type = null;
        return false;
    }

    /// <remarks>
    /// Targets are frozen at objective assignment time. Entities spawned after assignment
    /// (new APCs, cameras placed by crew) are intentionally excluded — the objective
    /// represents the station's state at moment of assignment.
    /// </remarks>
    private void SnapshotComponentEntities(List<EntityUid> targets, string componentName, MapId mapId)
    {
        if (!TryResolveComponentType(componentName, out var compType))
            return;

        // We must use the non-generic GetAllComponents(Type) overload here because the
        // component type is resolved at runtime from YAML prototype data via
        // TryResolveComponentType. EntityQueryEnumerator<T> requires a compile-time type
        // parameter. The _resolvedComponents cache mitigates repeated reflection costs,
        // and this method only runs at objective assignment time, not every tick.
        var xformQuery = GetEntityQuery<TransformComponent>();
        foreach (var (entUid, _) in EntityManager.GetAllComponents(compType))
        {
            if (xformQuery.GetComponent(entUid).MapID == mapId)
                targets.Add(entUid);
        }
    }

    private void SnapshotSecondaryEntities(List<EntityUid> targets, string componentName, MapId mapId, string? channelFilter)
    {
        if (!TryResolveComponentType(componentName, out var compType))
            return;

        var xformQuery = GetEntityQuery<TransformComponent>();
        foreach (var (entUid, comp) in EntityManager.GetAllComponents(compType))
        {
            if (xformQuery.GetComponent(entUid).MapID != mapId)
                continue;

            if (channelFilter != null
                && comp is EncryptionKeyComponent keyComp
                && !keyComp.Channels.Contains(channelFilter))
                continue;

            targets.Add(entUid);
        }
    }
}
