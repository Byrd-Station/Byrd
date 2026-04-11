// SPDX-FileCopyrightText: 2026 Raze500
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Omu.Shared.Resomi.Components;

/// <summary>
///     allows an entity to curl up and heal, like sleeping in a medical bed.
///     nesting is a simple toggle action - no partner required.
///     cannot be used while handcuffed.
///     there is a short wind-up and wind-down time to discourage spamming.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class NestingComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsNesting;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan NextHeal = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     how long the wind-up takes before the nest state actually changes.
    ///     prevents spamming the toggle to farm restfulness ticks.
    /// </summary>
    [DataField]
    public TimeSpan WindUpTime = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     when the pending nest toggle will complete. null means no toggle in progress.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan? PendingToggleAt;

    /// <summary>single toggle action - enters or exits the nest depending on current state.</summary>
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId ToggleNestAction;

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleNestActionEntity;

    /// <summary>how much the entity heals per update interval while nesting.</summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier HealingPerUpdate = new();

    [DataField, AutoNetworkedField]
    public float BleedHealPerUpdate = 0.5f;

    [DataField, AutoNetworkedField]
    public float CritHealingModifier = 1.5f;

    [DataField, AutoNetworkedField]
    public SoundSpecifier NestEnterSound = new SoundCollectionSpecifier("ResomiChirp");

    [DataField, AutoNetworkedField]
    public SoundSpecifier NestExitSound = new SoundCollectionSpecifier("ResomiChirp");

    /// <summary>flat damage reduction while nesting. 0.5 = 50% less incoming damage.</summary>
    [DataField, AutoNetworkedField]
    public float NestDamageReduction = 0.5f;

    [DataField, AutoNetworkedField]
    public EntProtoId NestEnterEffect = "EffectResomiNestEnter";

    [DataField, AutoNetworkedField]
    public EntProtoId NestContinuousEffect = "EffectResomiNestCurrent";

    [DataField, AutoNetworkedField]
    public EntProtoId NestExitEffect = "EffectResomiNestExit";

    [DataField, AutoNetworkedField]
    public EntityUid? ContinuousEffectEntity;
}
