// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Omu.Shared.Resomi.Components;

/// <summary>
/// Tracks a Resomi's Energy bar — a passive resource that drains over time
/// and recharges only while nesting. When depleted, applies a speed debuff.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ResomiEnergyComponent : Component
{
    /// <summary>Current energy level (0 – MaxEnergy).</summary>
    [DataField, AutoNetworkedField]
    public float Energy = 100f;

    [DataField]
    public float MaxEnergy = 100f;

    /// <summary>Energy lost per second while NOT nesting. Empties in ~20 minutes.</summary>
    [DataField]
    public float DrainRate = 100f / (20f * 60f);

    /// <summary>Energy restored per second while nesting. Fills in ~2 minutes.</summary>
    [DataField]
    public float RechargeRate = 100f / (2f * 60f);

    /// <summary>Speed modifier applied when energy reaches 0.</summary>
    [DataField]
    public float ExhaustionSpeedMultiplier = 0.70f;

    /// <summary>Energy threshold below which the Low alert is shown.</summary>
    [DataField]
    public float LowThreshold = 50f;

    /// <summary>Energy threshold below which the Danger alert is shown.</summary>
    [DataField]
    public float DangerThreshold = 20f;

    [DataField]
    public ProtoId<AlertPrototype> EnergyAlert = "ResomiEnergy";

    [DataField]
    public ProtoId<AlertCategoryPrototype> EnergyAlertCategory = "ResomiEnergy";

    /// <summary>True while energy is 0 and the speed debuff is active.</summary>
    [DataField, AutoNetworkedField]
    public bool IsExhausted;
}
