// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.GameObjects;

using Content.Server._Omu.Saboteur.Systems;

namespace Content.Server._Omu.Saboteur.Components;

/// <summary>
/// Tracks which saboteur objectives need re-evaluation after a game-state change.
/// Attached to the same entity as <see cref="SaboteurRuleComponent"/>.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurConditionCoreSystem), typeof(SaboteurRuleSystem))]
public sealed partial class SaboteurDirtyTrackingComponent : Component
{
    /// <summary>
    /// Maps a dirty domain flag to the list of objective UIDs interested in that domain.
    /// </summary>
    [DataField, ViewVariables]
    public Dictionary<SaboteurDirtyDomain, List<EntityUid>> InterestedObjectives = new();

    /// <summary>
    /// Set of objective UIDs that currently need their progress re-evaluated.
    /// </summary>
    [DataField, ViewVariables]
    public HashSet<EntityUid> DirtyObjectives = new();

    /// <summary>
    /// Dirty domains queued this tick but not yet flushed into <see cref="DirtyObjectives"/>.
    /// Each set bit represents a domain whose interested objectives must be marked dirty.
    /// </summary>
    [DataField, ViewVariables]
    public SaboteurDirtyDomain PendingDirtyDomains;

    /// <summary>
    /// Cached progress values keyed by objective cache key; invalidated when an objective becomes dirty.
    /// </summary>
    [DataField, ViewVariables]
    public Dictionary<string, float> ProgressCache = new();

    /// <summary>
    /// Objective UIDs whose cached progress reached 1.0 via any evaluation path
    /// (including external UI queries) but have not yet been processed by the
    /// completion sweep. Unlike <see cref="DirtyObjectives"/>, entries here are
    /// never consumed by progress queries — only the sweep clears them.
    /// </summary>
    [DataField, ViewVariables]
    public HashSet<EntityUid> PendingCompletions = new();

    /// <summary>
    /// Set by <see cref="SaboteurConditionCoreSystem.MarkDirty"/> when any domain
    /// becomes dirty. The rule system clears this after running the completion sweep.
    /// </summary>
    [DataField, ViewVariables]
    public bool CompletionSweepNeeded;

    /// <summary>
    /// Set by <see cref="SaboteurConditionCoreSystem.MarkDirty"/> when the
    /// <see cref="SaboteurDirtyDomain.Records"/> domain becomes dirty.
    /// The rule system clears this after running exposure checks.
    /// </summary>
    [DataField, ViewVariables]
    public bool ExposureCheckNeeded;
}
