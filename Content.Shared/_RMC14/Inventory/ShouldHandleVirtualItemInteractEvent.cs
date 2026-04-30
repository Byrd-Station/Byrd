// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Interaction;

namespace Content.Shared._RMC14.Inventory;

[ByRefEvent]
public record struct ShouldHandleVirtualItemInteractEvent(BeforeRangedInteractEvent Event, bool Handle = false);