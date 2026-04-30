// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Inventory;

/// <summary>
/// Items that can be marked in a <see cref="RMCPickupDroppedItemsComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedCMInventorySystem))]
public sealed partial class RMCItemPickupComponent : Component { }