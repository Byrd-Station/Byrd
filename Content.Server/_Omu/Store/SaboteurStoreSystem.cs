// SPDX-FileCopyrightText: 2026 Eponymic-sys
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Omu.Saboteur;
using Content.Server._Omu.Saboteur.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Shared._Omu.Saboteur;
using Content.Shared._Omu.Store;
using Content.Shared.Mind;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Omu.Store;

/// <summary>
/// Filters tier-locked listings out of the saboteur's store via a broadcast event.
/// </summary>
public sealed class SaboteurStoreSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SaboteurConditionCoreSystem _conditionCore = default!;

    private readonly HashSet<ListingData> _filteredListings = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StoreListingsPostFilterEvent>(OnStoreListingsPostFilter);
    }

    /// <summary>
    /// Removes listings that require a tier the saboteur hasn't reached yet.
    /// </summary>
    private void OnStoreListingsPostFilter(ref StoreListingsPostFilterEvent ev)
    {
        var buyer = ev.Component.AccountOwner ?? ev.Buyer;
        if (!HasComp<SaboteurComponent>(buyer))
            return;

        // ReputationTier lives on SaboteurMindComponent, not on the zero-field marker SaboteurComponent.
        if (!_mind.TryGetMind(buyer, out var mindId, out _))
            return;

        if (!TryComp<SaboteurMindComponent>(mindId, out var saboteurMind))
            return;

        if (!_conditionCore.TryGetRule(out var rule))
            return;

        _filteredListings.Clear();
        foreach (var listing in ev.Listings)
        {
            var requiredTier = GetRequiredTierForCategories(rule.CategoryTierMap, listing.Categories);
            if (requiredTier <= saboteurMind.ReputationTier)
                _filteredListings.Add(listing);
        }

        if (_filteredListings.Count < ev.Listings.Count)
        {
            // Copy into a new set so downstream consumers don't hold a reference
            // to our scratch _filteredListings, which gets .Clear()'d on the next call.
            var result = new HashSet<ListingData>(_filteredListings);
            ev.Listings = result;
            ev.Component.LastAvailableListings = result;
        }
    }

    /// <summary>
    /// Returns the highest tier required by any category in the listing.
    /// </summary>
    private static int GetRequiredTierForCategories(
        Dictionary<ProtoId<StoreCategoryPrototype>, int> categoryTierMap,
        List<ProtoId<StoreCategoryPrototype>> categories)
    {
        var maxTier = 0;
        foreach (var categoryId in categories)
        {
            if (categoryTierMap.TryGetValue(categoryId, out var tier))
                maxTier = Math.Max(maxTier, tier);
        }
        return maxTier;
    }
}
