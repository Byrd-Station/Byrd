// SPDX-FileCopyrightText: 2026 Eponymic-sys
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;
using Content.Omu.Shared.Chemistry.ConstantReagentFeed;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Omu.Server.Chemistry.ConstantReagentFeed;

// Server-side system that periodically injects configured reagents into a target solution.
public sealed class ConstantReagentFeedSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainers = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Iterate entities that both define a constant feed and own solution containers.
        var query = EntityQueryEnumerator<ConstantReagentFeedComponent, SolutionContainerManagerComponent>();
        while (query.MoveNext(out var uid, out var comp, out _))
        {
            // Skip entities with no configured reagents or invalid update intervals.
            if (comp.ReagentsPerSecond.Count == 0)
                continue;

            if (comp.UpdateInterval <= 0f)
                continue;

            // Accumulate time and only process when we cross the interval boundary.
            comp.Accumulator += frameTime;
            if (comp.Accumulator < comp.UpdateInterval)
                continue;

            // Determine how many full ticks have elapsed and retain the remainder.
            var updates = (int) (comp.Accumulator / comp.UpdateInterval);
            comp.Accumulator -= updates * comp.UpdateInterval;

            if (updates <= 0)
                continue;

            // Resolve the injectable solution on the entity (if any).
            if (!_solutionContainers.TryGetInjectableSolution(uid, out var targetSoln, out var targetSolution))
                continue;

            var totalSeconds = updates * comp.UpdateInterval;
            if (totalSeconds <= 0f)
                continue;

            // Build a temporary solution capped by the remaining available volume.
            var solution = new Solution
            {
                MaxVolume = targetSolution.AvailableVolume
            };

            // Add each reagent scaled by elapsed time, clamped to available volume.
            foreach (var item in comp.ReagentsPerSecond)
            {
                if (solution.AvailableVolume <= FixedPoint2.Zero)
                    break;

                var amount = item.Value * totalSeconds;
                if (amount <= FixedPoint2.Zero)
                    continue;

                amount = FixedPoint2.Min(amount, solution.AvailableVolume);
                solution.AddReagent(item.Key, amount);
            }

            // Only attempt injection if we actually added anything.
            if (solution.Volume > FixedPoint2.Zero)
                _solutionContainers.TryAddSolution(targetSoln.Value, solution);
        }
    }
}
