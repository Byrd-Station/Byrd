// SPDX-FileCopyrightText: 2025 RichardBlonski <48651647+RichardBlonski@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared._Omu.AdminEvents.TemuViro.Components;
[RegisterComponent, Serializable, NetSerializable, AutoGenerateComponentState]

public sealed partial class TemuViroComponent : Component
{
    /// <summary>
    /// The ID of the chemical that cures this condition.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public string CureChemical = "CE235"; // Custom Chem

    /// <summary>
    /// The minimum time at which the status effect can be applied to the player.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan MinEffectTime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The maximum time at which the status effect can be applied to the player.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan MaxEffectTime = TimeSpan.FromSeconds(5);
    /// <summary>
    /// The actual calculated time when the effect will be applied.
    /// This is set when the component is added to an entity.
    /// </summary>
    [DataField]
    public TimeSpan EffectTime { get; set; }

    /// <summary>
    /// Current amount of poison damage accumulated.
    /// </summary>
    [DataField]
    public float PoisonDamage { get; set; }

    /// <summary>
    /// Maximum amount of poison damage that can be accumulated.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MaxPoisonDamage { get; set; } = 50f;

    /// <summary>
    /// Minimum poison damage applied per vomiting event.
    /// </summary>
    [DataField]
    public int MinPoisonDamagePerEffect { get; set; } = 1;

    /// <summary>
    /// Maximum poison damage applied per vomiting event.
    /// </summary>
    [DataField]
    public int MaxPoisonDamagePerEffect { get; set; } = 5;

    /// <summary>
    /// Maximum drunkenness intensity at max poison damage.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public float MaxDrunkenness = 5f;

    /// <summary>
    /// Current drunkenness effect value.
    /// </summary>
    [ViewVariables]
    public float CurrentDrunkenness => Math.Clamp(
        PoisonDamage / MaxPoisonDamage * MaxDrunkenness,
        0f,
        MaxDrunkenness
    );



    #region Networked stuff
    /// <summary>
    /// Percentage chance (0-100) for the effect to trigger each interval
    /// </summary>
    [DataField, AutoNetworkedField]
    public double RandomChance { get; set; }

    /// <summary>
    /// Current progress towards being cured.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CureProgress { get; set; }

    /// <summary>
    /// Total amount of cure chemical needed to be cured.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float CureAmountNeeded { get; set; } = 5;

    /// <summary>
    /// Whether the condition has been cured.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool IsCured { get; set; }
    #endregion
}
