// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 to4no_fix <156101927+chavonadelal@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Clothing.EntitySystems;
using Robust.Shared.GameStates;

namespace Content.Shared.Clothing.Components;

/// <summary>
///     The component prohibits the player from taking off clothes on them that have this component.
/// </summary>
/// <remarks>
///     See also ClothingComponent.EquipDelay if you want the clothes that the player cannot take off by himself to be put on by the player with a delay.
///</remarks>
[NetworkedComponent]
[RegisterComponent]
[Access(typeof(SelfUnremovableClothingSystem))]
public sealed partial class SelfUnremovableClothingComponent : Component
{
    /// <summary>
    ///     How much electric shock damage to deal to anyone who tries to strip this item
    ///     without the required access. Set to 0 to disable the shock entirely.
    /// </summary>
    [DataField]
    public int UnauthorizedStripShockDamage = 0;

    /// <summary>
    ///     How long (in seconds) to stun the unauthorized stripper after shocking them.
    /// </summary>
    [DataField]
    public float UnauthorizedStripShockTime = 3f;
}

/// <summary>
///     Raised on the item when the shared system blocks an unauthorized strip attempt.
///     Server systems subscribe to this instead of BeingUnequippedAttemptEvent so we
///     don't have two systems trying to subscribe to the same (component, event) pair,
///     which SS14 forbids with a fatal Duplicate Subscriptions error.
/// </summary>
public sealed class SelfUnremovableClothingUnauthorizedStripEvent : EntityEventArgs
{
    /// <summary>The entity that attempted the strip and should be punished.</summary>
    public EntityUid Stripper;
}