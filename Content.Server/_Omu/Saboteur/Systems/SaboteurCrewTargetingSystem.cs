// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server._Omu.Saboteur.Conditions;
using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Shared._Omu.Saboteur;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.StationRecords;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Systems;

/// <summary>
/// Provides crew-targeting helpers used by saboteur objective condition systems
/// to pick, validate, and repick crew targets for frame/plant-evidence objectives.
/// </summary>
public sealed class SaboteurCrewTargetingSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    /// <remarks>Not reentrant-safe — must not be used from callbacks that also use this buffer.</remarks>
    private readonly HashSet<EntityUid> _scratchTargeted = new();
    /// <remarks>Not reentrant-safe — must not be used from callbacks that also use this buffer.</remarks>
    private readonly List<CrewTarget> _scratchCrewTargets = new();
    /// <remarks>Not reentrant-safe — must not be used from callbacks that also use this buffer.</remarks>
    private readonly List<EntityUid> _scratchCandidates = new();
    /// <remarks>Not reentrant-safe — must not be used from callbacks that also use this buffer.</remarks>
    private readonly List<string> _scratchNames = new();

    /// <summary>
    /// Collects all crew members who are eligible for targeting by saboteur objectives.
    /// </summary>
    /// <param name="saboteur">The saboteur's entity — excluded from results.</param>
    /// <param name="results">Output list populated with eligible crew targets.</param>
    /// <param name="excludeTargeted">Optional set of entities already targeted — excluded from results.</param>
    public void CollectEligibleCrew(EntityUid? saboteur, List<CrewTarget> results, HashSet<EntityUid>? excludeTargeted = null)
    {
        var idQuery = EntityQueryEnumerator<IdCardComponent>();
        while (idQuery.MoveNext(out var entUid, out var idCard))
        {
            var holder = GetCardHolder(entUid);
            if (holder == null)
                continue;

            if (saboteur.HasValue && holder.Value == saboteur.Value)
                continue;

            if (HasComp<SaboteurComponent>(holder.Value))
                continue;

            if (excludeTargeted != null && excludeTargeted.Contains(holder.Value))
                continue;

            if (!TryComp<StationRecordKeyStorageComponent>(entUid, out var keyStorage)
                && _idCard.TryFindIdCard(holder.Value, out var foundIdCard))
            {
                TryComp(foundIdCard, out keyStorage);
            }

            if (keyStorage?.Key is not { } key)
                continue;

            results.Add(new CrewTarget(holder.Value, entUid, idCard, key));
        }
    }

    /// <summary>
    /// Populates <paramref name="results"/> with entities already targeted by any
    /// <see cref="SaboteurCrewTargetDataComponent"/> objectives on the given mind.
    /// </summary>
    public void GetAlreadyTargetedEntities(EntityUid mindId, HashSet<EntityUid> results)
    {
        results.Clear();

        if (!TryComp<MindComponent>(mindId, out var mind))
            return;

        foreach (var objUid in mind.Objectives)
        {
            if (!TryComp<SaboteurCrewTargetDataComponent>(objUid, out var data))
                continue;

            foreach (var target in data.FlagTargets)
            {
                results.Add(target);
            }
        }
    }

    /// <summary>
    /// Performs the common after-assign initialization for crew-target conditions:
    /// generates a cache key, snapshots the saboteur's display name, and registers
    /// dirty-domain interests. Ensures <see cref="SaboteurCrewTargetDataComponent"/> exists on the entity.
    /// </summary>
    public void InitializeAssignment(
        EntityUid uid,
        ObjectiveAfterAssignEvent args,
        SaboteurDirtyTrackingComponent dirty,
        SaboteurDirtyDomain domains)
    {
        var data = EnsureComp<SaboteurCrewTargetDataComponent>(uid);

        data.CacheKey = _core.MakeCacheKey(uid);

        if (data.FlagTargets.Count > 0)
            data.OriginalFlagTargetCount = data.FlagTargets.Count;

        if (args.Mind.OwnedEntity.HasValue)
            data.SnapshottedName = Identity.Name(args.Mind.OwnedEntity.Value, EntityManager);

        _core.RegisterInterest(dirty, uid, domains);
    }

    /// <summary>
    /// Collects eligible crew, filters with a caller-provided predicate (using
    /// <typeparamref name="TState"/> to avoid lambda captures), shuffles, and picks
    /// up to <paramref name="requiredCount"/> targets into <paramref name="outTargets"/>.
    /// Returns <c>false</c> if fewer than <paramref name="requiredCount"/> candidates pass the filter.
    /// </summary>
    public bool TryPickCrewTargets<TState>(
        EntityUid mindId,
        EntityUid? saboteur,
        List<EntityUid> outTargets,
        int requiredCount,
        Func<CrewTarget, TState, bool> predicate,
        TState state)
    {
        _scratchTargeted.Clear();
        _scratchCrewTargets.Clear();
        _scratchCandidates.Clear();

        GetAlreadyTargetedEntities(mindId, _scratchTargeted);
        CollectEligibleCrew(saboteur, _scratchCrewTargets, _scratchTargeted);

        foreach (var ct in _scratchCrewTargets)
        {
            if (predicate(ct, state))
                _scratchCandidates.Add(ct.Holder);
        }

        if (_scratchCandidates.Count < requiredCount)
        {
            _scratchTargeted.Clear();
            _scratchCrewTargets.Clear();
            _scratchCandidates.Clear();
            return false;
        }

        _random.Shuffle(_scratchCandidates);
        for (var i = 0; i < requiredCount && i < _scratchCandidates.Count; i++)
        {
            outTargets.Add(_scratchCandidates[i]);
        }

        _scratchTargeted.Clear();
        _scratchCrewTargets.Clear();
        _scratchCandidates.Clear();
        return true;
    }

    /// <summary>
    /// Updates the objective description using the names of crew targets,
    /// formatted by a caller-provided builder to avoid hard-coding loc parameter names.
    /// <paramref name="buildLocArgs"/> receives the collected target names and returns
    /// the loc arguments to interpolate into the description.
    /// </summary>
    public void UpdateCrewTargetDescription(
        EntityUid uid,
        SaboteurCrewTargetDataComponent data,
        MetaDataComponent meta,
        Func<List<string>, (string, object)[]> buildLocArgs)
    {
        if (data.FlagTargets.Count == 0)
            return;

        if (!TryComp<SaboteurOperationComponent>(uid, out var opComp))
            return;

        _scratchNames.Clear();
        foreach (var target in data.FlagTargets)
        {
            if (EntityManager.EntityExists(target))
                _scratchNames.Add(Identity.Name(target, EntityManager));
        }

        if (_scratchNames.Count == 0)
            return;

        var proto = _prototypeManager.Index(opComp.OperationId);
        var ftlBase = proto.LocId;
        var locArgs = buildLocArgs(_scratchNames);
        _metaData.SetEntityDescription(uid, Loc.GetString($"{ftlBase}-desc", locArgs), meta);
        _scratchNames.Clear();
    }

    /// <summary>
    /// Removes deleted entities from <paramref name="flagTargets"/> and attempts to
    /// replace them with new eligible crew members that pass the caller-provided
    /// <paramref name="predicate"/>.
    /// </summary>
    public void TryRepickDeletedTargets<TState>(
        List<EntityUid> flagTargets,
        EntityUid mindId,
        Func<CrewTarget, TState, bool> predicate,
        TState state)
    {
        var removedCount = 0;
        for (var i = flagTargets.Count - 1; i >= 0; i--)
        {
            if (!EntityManager.EntityExists(flagTargets[i]))
            {
                flagTargets.RemoveAt(i);
                removedCount++;
            }
        }

        if (removedCount == 0)
            return;

        _scratchTargeted.Clear();
        _scratchCrewTargets.Clear();
        _scratchCandidates.Clear();

        GetAlreadyTargetedEntities(mindId, _scratchTargeted);

        foreach (var existing in flagTargets)
        {
            _scratchTargeted.Add(existing);
        }

        EntityUid? saboteur = null;
        if (TryComp<MindComponent>(mindId, out var mind))
            saboteur = mind.OwnedEntity;

        CollectEligibleCrew(saboteur, _scratchCrewTargets, _scratchTargeted);

        foreach (var ct in _scratchCrewTargets)
        {
            if (predicate(ct, state))
                _scratchCandidates.Add(ct.Holder);
        }

        if (_scratchCandidates.Count > 0)
        {
            _random.Shuffle(_scratchCandidates);
            for (var i = 0; i < removedCount && i < _scratchCandidates.Count; i++)
            {
                flagTargets.Add(_scratchCandidates[i]);
            }
        }

        _scratchTargeted.Clear();
        _scratchCrewTargets.Clear();
        _scratchCandidates.Clear();
    }

    public EntityUid? GetCardHolder(EntityUid cardUid)
    {
        var current = cardUid;
        while (_container.TryGetContainingContainer(current, out var container))
        {
            if (HasComp<MobStateComponent>(container.Owner))
                return container.Owner;
            current = container.Owner;
        }
        return null;
    }
}
