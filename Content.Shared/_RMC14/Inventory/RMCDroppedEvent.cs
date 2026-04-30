// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._RMC14.Inventory;

[ByRefEvent]
public readonly record struct RMCDroppedEvent(EntityUid User);