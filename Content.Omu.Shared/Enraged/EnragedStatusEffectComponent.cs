// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Omu.Shared.Enraged;

/// <summary>
/// Tracks the enraged status effect state and configuration.
/// Stores hostility settings and restoration data for when the effect expires.
/// </summary>
[RegisterComponent]
public sealed partial class EnragedStatusEffectComponent : Component
{
    [DataField]
    public string HostileFaction = "Hostile";

    [DataField]
    public string HostileRootTask = "SimpleHostileCompound";
    public bool RemovedSsdIndicator;
    public bool AddedFactionComponent;
    public bool AddedHtnComponent;
    public HashSet<string> OldFactions = new();
    public string? OldRootTask;
}
