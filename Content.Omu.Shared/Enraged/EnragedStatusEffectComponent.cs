// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Omu.Shared.Enraged;

[RegisterComponent]
public sealed partial class EnragedStatusEffectComponent : Component
{
    // Faction to apply while the status effect is active.
    [DataField]
    public ProtoId<NpcFactionPrototype> HostileFaction = "SimpleHostile";

    // HTN root task used to drive hostile simple-mob behavior.
    [DataField]
    public string HostileRootTask = "SimpleHostileCompound";

    // Factions to restore once the effect ends.
    [ViewVariables(VVAccess.ReadOnly)]
    public HashSet<ProtoId<NpcFactionPrototype>> OldFactions = new();

    // Previous HTN root task to restore once the effect ends.
    [ViewVariables(VVAccess.ReadOnly)]
    public string? OldRootTask;

    // Tracks whether this effect added the faction component.
    [ViewVariables(VVAccess.ReadOnly)]
    public bool AddedFactionComponent;

    // Tracks whether this effect added the HTN component.
    [ViewVariables(VVAccess.ReadOnly)]
    public bool AddedHtnComponent;

    // Tracks whether SSD indicator was removed while the effect is active.
    [ViewVariables(VVAccess.ReadOnly)]
    public bool RemovedSsdIndicator;

}
