// SPDX-FileCopyrightText: 2026 Eponymic-sys
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Store;
using Content.Shared.Store.Components;

namespace Content.Shared._Omu.Store;

/// <summary>
/// Raised as a broadcast event after available listings have been calculated for a store UI update.
/// Subscribers can modify the <see cref="Listings"/> set (e.g. remove tier-locked items for saboteurs).
/// </summary>
/// <remarks>
/// This event is raised from an upstream patch site in
/// <c>Content.Server/Store/Systems/StoreSystem.Ui.cs</c> inside <c>RefreshAllListings</c>.
/// Any changes to that method's listing-generation flow may move or remove the raise site.
/// </remarks>
[ByRefEvent]
public record struct StoreListingsPostFilterEvent(
    EntityUid Buyer,
    EntityUid Store,
    StoreComponent Component,
    HashSet<ListingData> Listings
);
