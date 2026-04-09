// SPDX-FileCopyrightText: 2026 Raze500
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Omu.Shared.Firearms.Components;

/// <summary>
///     when added to a gun, applies a physics knockback impulse to the shooter
///     in the opposite direction of fire each time the gun is shot.
///     generic - can be added to any gun prototype via yaml.
///     designed to represent recoil from heavy weapons for smaller species.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FirearmKickbackComponent : Component
{
    /// <summary>
    ///     how strong the knockback impulse is. higher = more pushback.
    ///     a value around 100-200 is noticeable but not crippling.
    /// </summary>
    [DataField]
    public float KickbackStrength = 150f;
}
