// SPDX-FileCopyrightText: 2026 Raze500
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Omu.Shared.ExtraResource.Components;

/// <summary>
///     generic component for species-specific passive resources beyond hunger and thirst.
///     each resource lives in the Resources dictionary under a string key (e.g. "restfulness").
///     the system checks for known keys and applies the matching gameplay logic.
///     this keeps the c# generic - adding a new resource for another species only needs
///     a yaml entry and a key check in ExtraResourceSystem.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExtraResourceComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<string, ExtraResourceEntry> Resources = new();
}

/// <summary>
///     data for a single extra resource entry.
/// </summary>
[DataDefinition]
public sealed partial class ExtraResourceEntry
{
    /// <summary>current value, clamped between 0 and Max.</summary>
    [DataField]
    public float Current = 100f;

    [DataField]
    public float Max = 100f;

    /// <summary>
    ///     alert prototype to show in the hud. leave empty to show no alert.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> Alert = default;

    [DataField]
    public ProtoId<AlertCategoryPrototype> AlertCategory = default;
}
