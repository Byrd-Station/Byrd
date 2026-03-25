// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._Omu.Resomi.Components;

/// <summary>
/// Marks a Resomi as subject to combat debuffs that reflect their small, fragile build.
/// Resomis are fast couriers and scouts, not soldiers — this component enforces that identity
/// by making prolonged combat riskier and less effective than for other species.
///
/// See <see cref="ResomiCombatDebuffSystem"/> for the actual application logic.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ResomiCombatDebuffComponent : Component
{
    /// <summary>
    /// Multiplier applied to outgoing melee damage.
    /// Default 0.85 = 15% less damage — lighter body mass means weaker physical strikes.
    /// </summary>
    [DataField]
    public float MeleeDamageMultiplier = 0.85f;

    /// <summary>
    /// Multiplier applied to camera recoil shake when firing a gun.
    /// Default 1.75 = 75% more recoil feedback.
    /// Combined with SpreadIncreaseDegrees for a full "bad handling" feel.
    /// </summary>
    [DataField]
    public float RecoilMultiplier = 1.75f;

    /// <summary>
    /// Flat degrees added to both MinAngle and MaxAngle on every gun refresh.
    /// Default 8 degrees — clearly noticeable at range, not crippling up close.
    /// Raise to 10+ for a more severe penalty; lower to 4–5 for a subtle one.
    /// </summary>
    [DataField]
    public float SpreadIncreaseDegrees = 8f;
}