// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Omu.Shared.Resomi.Components;

/// <summary>
/// Gives a Resomi a sprint ability that drains the entity's real stamina.
/// If stamcrit triggers while sprinting, the Resomi suffers a 20-second
/// massive speed debuff after recovering.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ResomiImpulseComponent : Component
{
    /// <summary>Stamina drained per second via ActiveDrains while sprinting.</summary>
    [DataField]
    public float StaminaDrainRate = 18f;

    /// <summary>Key used in StaminaComponent.ActiveDrains.</summary>
    public const string DrainKey = "ResomiSprint";

    [DataField, AutoNetworkedField]
    public bool IsSprinting;

    /// <summary>True while waiting for the stamcrit to end (before the 20s debuff starts).</summary>
    [DataField, AutoNetworkedField]
    public bool IsExhaustedPending;

    /// <summary>True during the 20-second post-stamcrit speed debuff.</summary>
    [DataField, AutoNetworkedField]
    public bool IsExhausted;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan ExhaustionEnd = TimeSpan.Zero;

    /// <summary>Speed multiplier applied while sprinting (e.g. 1.5 = 50% faster).</summary>
    [DataField]
    public float SprintSpeedMultiplier = 1.5f;

    /// <summary>Speed multiplier applied during the exhaustion debuff (e.g. 0.4 = 60% slower).</summary>
    [DataField]
    public float ExhaustionSpeedMultiplier = 0.4f;

    /// <summary>How long the post-stamcrit speed debuff lasts.</summary>
    [DataField]
    public TimeSpan ExhaustionDuration = TimeSpan.FromSeconds(20);

    [DataField(required: true), AutoNetworkedField]
    public EntProtoId SprintToggleAction;

    [DataField, AutoNetworkedField]
    public EntityUid? SprintToggleActionEntity;
}
