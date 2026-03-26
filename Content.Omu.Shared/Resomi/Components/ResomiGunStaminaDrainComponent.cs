// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Omu.Shared.Resomi.Components;

/// <summary>
/// Marker component for guns that drain a Resomi's stamina when fired.
/// Added to heavy weapon base prototypes (rifles, shotguns, snipers, launchers, HMGs, LMGs).
/// Pistols, revolvers, and SMGs deliberately do NOT have this component.
///
/// Stamina drain is applied by <see cref="EntitySystems.ResomiCombatDebuffSystem"/>
/// when the wielder has <see cref="ResomiCombatDebuffComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class ResomiGunStaminaDrainComponent : Component
{
    /// <summary>
    /// How much stamina is drained from the Resomi per shot fired.
    /// With a max of 100 stamina, 30 = stamcrit after roughly 3–4 shots.
    /// </summary>
    [DataField]
    public float StaminaDrain = 30f;
}
