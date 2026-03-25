// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Omu.Resomi.Components;

/// <summary>
/// Gives a Resomi the ability to emit a directional call that all other
/// Resomis on the same map can hear, receiving the caller's name and direction.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ResomiCallComponent : Component
{
    [DataField(required: true)]
    public EntProtoId CallAction;

    [DataField]
    public EntityUid? CallActionEntity;

    /// <summary>Cooldown between calls.</summary>
    [DataField]
    public TimeSpan CallCooldown = TimeSpan.FromSeconds(60);
}