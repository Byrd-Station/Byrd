// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Common.MartialArts;
using Content.Server.NPC.Components;

namespace Content.Omu.Server.MartialArts;

/// <summary>
/// Transient component for NPC martial arts combat.
/// Holds target, combo progress, and status while an HTN plan is active.
/// </summary>
[RegisterComponent]
public sealed partial class NpcMartialArtsCombatComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public EntityUid Target;

    [ViewVariables]
    public CombatStatus Status = CombatStatus.Normal;

    [ViewVariables]
    public ComboPrototype? ActiveCombo;

    [ViewVariables]
    public int StepIndex;

    [ViewVariables]
    public float StepCooldown;

    [DataField]
    public float TimeBetweenSteps = 0.45f;

    [ViewVariables]
    public TimeSpan LastStepTime;

    [DataField]
    public TimeSpan StepTimeout = TimeSpan.FromSeconds(2);

    [ViewVariables]
    public int FillerAttacksRemaining;

    [DataField]
    public int FillerAttacksMin = 1;

    [DataField]
    public int FillerAttacksMax = 3;
}
