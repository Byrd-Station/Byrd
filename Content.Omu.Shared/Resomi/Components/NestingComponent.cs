// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Omu.Shared.Resomi.Components;

/// <summary>
/// Allows a Resomi to curl up in a nest, becoming immobile but slowly healing.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class NestingComponent : Component
{
    /// <summary>
    /// Whether the entity is currently nesting.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsNesting;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan NextHeal = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The action granted to enter the nest.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId EnterNestAction;

    /// <summary>
    /// The action granted to exit the nest.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId ExitNestAction;

    [DataField, AutoNetworkedField]
    public EntityUid? EnterNestActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? ExitNestActionEntity;

    /// <summary>
    /// Cooldown before the Resomi can nest again after exiting.
    /// </summary>
    [DataField]
    public TimeSpan NestCooldown = TimeSpan.FromSeconds(120);

    /// <summary>
    /// How much the entity heals per update interval while nesting.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier HealingPerUpdate = new();

    /// <summary>
    /// How much bleed is healed per update interval while nesting.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BleedHealPerUpdate = 0.5f;

    /// <summary>
    /// How much extra healing is done when the entity is in a critical state.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CritHealingModifier = 1.5f;

    /// <summary>
    /// Sound played when curling into the nest.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier NestEnterSound = new SoundCollectionSpecifier("ResomiChirp");

    /// <summary>
    /// Sound played when getting up from the nest.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier NestExitSound = new SoundCollectionSpecifier("ResomiChirp");

    /// <summary>
    /// Flat damage reduction multiplier applied to all incoming positive damage while nesting.
    /// 0.5 = 50% reduction (pain tolerance).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float NestDamageReduction = 0.5f;

    /// <summary>
    /// Effect spawned when entering the nest.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId NestEnterEffect = "EffectResomiNestEnter";

    /// <summary>
    /// Effect spawned while the nest is active (continuous).
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId NestContinuousEffect = "EffectResomiNestCurrent";

    /// <summary>
    /// Effect spawned when exiting the nest.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId NestExitEffect = "EffectResomiNestExit";

    /// <summary>
    /// Tracks the currently spawned continuous nest effect entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ContinuousEffectEntity;
}
