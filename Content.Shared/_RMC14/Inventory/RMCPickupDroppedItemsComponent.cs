// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Inventory;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedCMInventorySystem))]
public sealed partial class RMCPickupDroppedItemsComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<EntityUid> DroppedItems = new();
}