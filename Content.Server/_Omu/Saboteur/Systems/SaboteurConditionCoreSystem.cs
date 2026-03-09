// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server._Omu.Saboteur.Conditions;
using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Server.SurveillanceCamera;
using Content.Shared._Omu.Saboteur;
using Content.Shared.APC;
using Content.Shared.Doors.Components;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Systems;

/// <summary>
/// Core system for saboteur objective conditions: dirty-tracking infrastructure,
/// progress caching, entity-on-station checks, and sabotage-mode evaluation.
/// </summary>
public sealed class SaboteurConditionCoreSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _station = default!;

    private EntityQuery<SaboteurRuleComponent> _ruleQuery;
    private EntityQuery<SaboteurDirtyTrackingComponent> _dirtyQuery;

    /// <summary>
    /// Cached rule entity UID — set on <see cref="ComponentInit"/>, cleared on
    /// <see cref="ComponentRemove"/>. Avoids per-call enumeration on hot paths
    /// (condition progress checks, completion sweeps).
    /// </summary>
    private EntityUid? _cachedRuleUid;

    public override void Initialize()
    {
        base.Initialize();
        _ruleQuery = GetEntityQuery<SaboteurRuleComponent>();
        _dirtyQuery = GetEntityQuery<SaboteurDirtyTrackingComponent>();

        SubscribeLocalEvent<SaboteurRuleComponent, ComponentInit>(OnRuleInit);
        SubscribeLocalEvent<SaboteurRuleComponent, ComponentRemove>(OnRuleRemove);
    }

    private void OnRuleInit(EntityUid uid, SaboteurRuleComponent comp, ComponentInit args)
    {
        if (_cachedRuleUid != null)
            Log.Warning($"Multiple SaboteurRuleComponent entities detected ({ToPrettyString(_cachedRuleUid.Value)} and {ToPrettyString(uid)}). Only one should exist.");

        _cachedRuleUid = uid;
        EnsureComp<SaboteurDirtyTrackingComponent>(uid);
    }

    private void OnRuleRemove(EntityUid uid, SaboteurRuleComponent comp, ComponentRemove args)
    {
        if (_cachedRuleUid == uid)
            _cachedRuleUid = null;
    }

    /// <summary>
    /// Looks up the active saboteur rule using the cached entity UID
    /// and a pre-resolved <see cref="EntityQuery{T}"/> for fast access on hot paths.
    /// </summary>
    public bool TryGetRule([NotNullWhen(true)] out SaboteurRuleComponent? rule)
    {
        if (_cachedRuleUid is { } uid)
        {
            if (!EntityManager.EntityExists(uid))
            {
                _cachedRuleUid = null;
                rule = null;
                return false;
            }

            if (_ruleQuery.TryGetComponent(uid, out rule))
                return true;
        }

        rule = null;
        return false;
    }

    /// <summary>
    /// Looks up the dirty-tracking component on the active saboteur rule entity
    /// using the cached UID and a pre-resolved <see cref="EntityQuery{T}"/>.
    /// </summary>
    public bool TryGetDirtyTracking([NotNullWhen(true)] out SaboteurDirtyTrackingComponent? dirty)
    {
        if (_cachedRuleUid is { } uid)
        {
            if (!EntityManager.EntityExists(uid))
            {
                _cachedRuleUid = null;
                dirty = null;
                return false;
            }

            if (_dirtyQuery.TryGetComponent(uid, out dirty))
                return true;
        }

        dirty = null;
        return false;
    }

    public bool TryGetCached(SaboteurDirtyTrackingComponent dirty, EntityUid objectiveUid, string key, out float progress)
    {
        FlushPendingDirty(dirty);

        if (!dirty.DirtyObjectives.Contains(objectiveUid) && dirty.ProgressCache.TryGetValue(key, out progress))
            return true;
        progress = 0f;
        return false;
    }

    public float CacheAndReturn(SaboteurDirtyTrackingComponent dirty, EntityUid objectiveUid, string key, float progress)
    {
        dirty.DirtyObjectives.Remove(objectiveUid);
        dirty.ProgressCache[key] = progress;

        // Track completed objectives independently of the dirty flag so the
        // completion sweep always finds them, even when an external progress
        // query (e.g. the character-screen UI) consumed the dirty flag first.
        if (progress >= 1f)
            dirty.PendingCompletions.Add(objectiveUid);

        return progress;
    }

    /// <summary>
    /// Returns whether a previous progress evaluation cached a completion (>= 1.0)
    /// for this objective that the completion sweep has not yet processed.
    /// </summary>
    public bool IsCompletionPending(SaboteurDirtyTrackingComponent dirty, EntityUid objectiveUid)
        => dirty.PendingCompletions.Contains(objectiveUid);

    /// <summary>
    /// Clears the pending-completion flag after the sweep has processed it.
    /// </summary>
    public void ClearPendingCompletion(SaboteurDirtyTrackingComponent dirty, EntityUid objectiveUid)
        => dirty.PendingCompletions.Remove(objectiveUid);

    public void MarkDirty(SaboteurDirtyDomain domain)
    {
        if (!TryGetDirtyTracking(out var dirty))
            return;

        dirty.PendingDirtyDomains |= domain;
        dirty.CompletionSweepNeeded = true;

        if (domain == SaboteurDirtyDomain.Records)
            dirty.ExposureCheckNeeded = true;
    }

    /// <summary>
    /// All individual domain flag values, cached once to avoid repeated allocation
    /// from <see cref="Enum.GetValues{T}"/> on every flush.
    /// </summary>
    private static readonly SaboteurDirtyDomain[] AllDomains =
        Enum.GetValues<SaboteurDirtyDomain>().Where(d => d != SaboteurDirtyDomain.None).ToArray();

    public void FlushPendingDirty(SaboteurDirtyTrackingComponent dirty)
    {
        if (dirty.PendingDirtyDomains == SaboteurDirtyDomain.None)
            return;

        foreach (var domain in AllDomains)
        {
            if ((dirty.PendingDirtyDomains & domain) == 0)
                continue;

            if (!dirty.InterestedObjectives.TryGetValue(domain, out var interested))
                continue;

            for (var i = interested.Count - 1; i >= 0; i--)
            {
                var uid = interested[i];
                if (!EntityManager.EntityExists(uid))
                {
                    interested.RemoveAt(i);
                    continue;
                }
                dirty.DirtyObjectives.Add(uid);
            }
        }

        dirty.PendingDirtyDomains = SaboteurDirtyDomain.None;
    }

    public bool HasDirtyObjectives(SaboteurDirtyTrackingComponent dirty)
    {
        FlushPendingDirty(dirty);
        return dirty.DirtyObjectives.Count > 0;
    }

    public bool IsObjectiveDirty(SaboteurDirtyTrackingComponent dirty, EntityUid objectiveUid) => dirty.DirtyObjectives.Contains(objectiveUid);

    /// <summary>
    /// Registers interest for an objective in one or more dirty domains.
    /// Use the <see cref="SaboteurDirtyDomain"/> flags enum — combine
    /// multiple domains with bitwise OR.
    /// </summary>
    public void RegisterInterest(SaboteurDirtyTrackingComponent dirty, EntityUid objectiveUid, SaboteurDirtyDomain domains)
    {
        foreach (var domain in AllDomains)
        {
            if ((domains & domain) != 0)
                AddDomainInterest(dirty, objectiveUid, domain);
        }
        dirty.DirtyObjectives.Add(objectiveUid);
    }

    private void AddDomainInterest(SaboteurDirtyTrackingComponent dirty, EntityUid objectiveUid, SaboteurDirtyDomain domain)
    {
        if (!dirty.InterestedObjectives.TryGetValue(domain, out var list))
            dirty.InterestedObjectives[domain] = list = new List<EntityUid>();
        if (!list.Contains(objectiveUid))
            list.Add(objectiveUid);
    }

    public bool TryBeginProgressCheck(
        EntityUid objectiveUid,
        ref ObjectiveGetProgressEvent args,
        out EntityUid saboteurBody,
        [NotNullWhen(true)] out SaboteurDirtyTrackingComponent? dirty)
    {
        saboteurBody = default;
        dirty = null;

        args.Progress ??= 0f;

        if (!TryGetDirtyTracking(out dirty))
            return false;

        if (!MindOwnsObjective(objectiveUid, args.Mind))
            return false;

        if (args.Mind.OwnedEntity is not { } owned)
            return false;

        if (!IsSaboteurOnStation(owned))
            return false;

        saboteurBody = owned;
        return true;
    }

    public bool IsSaboteurOnStation(EntityUid saboteurBody)
    {
        return _station.GetOwningStation(saboteurBody) != null;
    }

    public bool TryGetAnyStationMapId(out MapId mapId)
    {
        foreach (var station in _station.GetStations())
        {
            var stationGrid = _station.GetLargestGrid(station);
            if (stationGrid == null)
                continue;

            mapId = Transform(stationGrid.Value).MapID;
            return true;
        }
        mapId = default;
        return false;
    }

    public bool IsEntityDisabled(EntityUid uid, SabotageMode mode)
    {
        return mode switch
        {
            SabotageMode.Unpowered =>
                !TryComp<ApcPowerReceiverComponent>(uid, out var power) || !power.Powered,
            SabotageMode.CameraInactive =>
                TryComp<SurveillanceCameraComponent>(uid, out var cam) && !cam.Active,
            SabotageMode.ApcDisabled =>
                TryComp<ApcComponent>(uid, out var apc)
                && (!apc.MainBreakerEnabled || apc.LastExternalState == ApcExternalPowerState.None),
            SabotageMode.KeysEmpty =>
                TryComp<EncryptionKeyHolderComponent>(uid, out var holder) && holder.KeyContainer.ContainedEntities.Count == 0,
            SabotageMode.Destroyed =>
                false,
            SabotageMode.DoorBolted =>
                TryComp<DoorBoltComponent>(uid, out var bolt) && bolt.BoltsDown,
            _ => false,
        };
    }

    #region Progress Helpers

    /// <summary>
    /// Generates a deterministic cache key for a given objective entity.
    /// </summary>
    public string MakeCacheKey(EntityUid objectiveUid) => $"cond_{objectiveUid.Id}";

    /// <summary>
    /// Returns the progress fraction for a threshold-based condition.
    /// </summary>
    public float CalculateThresholdProgress(int affected, int total, float threshold)
    {
        if (total <= 0 || threshold <= 0f)
            return 0f;

        var fraction = (float) affected / total;
        return Math.Min(1f, fraction / threshold);
    }

    /// <summary>
    /// Returns the progress fraction for a count-based condition.
    /// </summary>
    public float CountProgress(int count, int required)
    {
        if (count >= required)
            return 1f;

        return count > 0 ? (float) count / required : 0f;
    }

    /// <summary>
    /// Returns the dirty-tracking domain flag(s) that correspond to a given
    /// <see cref="SabotageMode"/>.
    /// </summary>
    public SaboteurDirtyDomain GetDomainsForCheckMode(SabotageMode mode)
    {
        return mode switch
        {
            SabotageMode.Unpowered or SabotageMode.ApcDisabled => SaboteurDirtyDomain.Power,
            SabotageMode.CameraInactive => SaboteurDirtyDomain.Camera,
            SabotageMode.KeysEmpty => SaboteurDirtyDomain.EncryptionKeyHolder | SaboteurDirtyDomain.Entity,
            SabotageMode.Destroyed => SaboteurDirtyDomain.Entity,
            SabotageMode.DoorBolted => SaboteurDirtyDomain.Bolts,
            _ => SaboteurDirtyDomain.None,
        };
    }

    /// <summary>
    /// Returns whether the given mind's objectives list contains the specified objective entity.
    /// </summary>
    public bool MindOwnsObjective(EntityUid objectiveUid, MindComponent mind)
    {
        var objectives = mind.Objectives;
        for (var i = 0; i < objectives.Count; i++)
        {
            if (objectives[i] == objectiveUid)
                return true;
        }
        return false;
    }

    #endregion
}
