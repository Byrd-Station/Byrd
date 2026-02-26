// SPDX-FileCopyrightText: 2026 Eponymic-sys
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;
using Content.Omu.Shared.Chemistry.ConstantReagentFeed;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;

namespace Content.Omu.Server.Chemistry.ConstantReagentFeed;

/// <summary>
/// Server-side system that periodically injects configured reagents into a
/// target solution, optionally pulling them from a source container.
/// <para/>
/// When <see cref="ConstantReagentFeedComponent.SourceEntity"/> is set the
/// system removes reagents from that entity's solution before injecting.
/// When it is null, reagents are generated from nothing (original behaviour).
/// </summary>
public sealed class ConstantReagentFeedSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainers = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ConstantReagentFeedComponent, SolutionContainerManagerComponent>();
        while (query.MoveNext(out var uid, out var comp, out _))
        {
            if (comp.ReagentsPerSecond.Count == 0)
                continue;

            if (comp.UpdateInterval <= 0f)
                continue;

            comp.Accumulator += frameTime;
            if (comp.Accumulator < comp.UpdateInterval)
                continue;

            var updates = (int) (comp.Accumulator / comp.UpdateInterval);
            comp.Accumulator -= updates * comp.UpdateInterval;

            if (updates <= 0)
                continue;

            // Resolve the injectable (target) solution on the entity.
            if (!_solutionContainers.TryGetInjectableSolution(uid, out var targetSoln, out var targetSolution))
                continue;

            var totalSeconds = updates * comp.UpdateInterval;
            if (totalSeconds <= 0f)
                continue;

            // Resolve the optional source solution.
            Solution? sourceSolution = null;
            Entity<SolutionComponent>? sourceSoln = null;

            if (comp.SourceEntity is { } sourceUid && Exists(sourceUid))
            {
                if (!TryResolveSource(sourceUid, comp.SourceSolutionName, out sourceSoln, out sourceSolution))
                    continue; // Source configured but not resolvable — skip this tick.
            }

            // Build the injection payload capped by target available volume.
            var injection = BuildInjection(comp, totalSeconds, targetSolution.AvailableVolume, sourceSolution);

            if (injection.Volume <= FixedPoint2.Zero)
                continue;

            // Remove consumed reagents from the source (if present).
            if (sourceSoln != null && sourceSolution != null)
                DrainSourceReagents(sourceSoln.Value, injection);

            _solutionContainers.TryAddSolution(targetSoln.Value, injection);
        }
    }

    /// <summary>
    /// Resolves the source solution on the given entity. Uses a named solution
    /// when specified, otherwise falls back to the drawable solution.
    /// </summary>
    private bool TryResolveSource(
        EntityUid sourceUid,
        string? solutionName,
        out Entity<SolutionComponent>? soln,
        out Solution? solution)
    {
        if (solutionName != null)
            return _solutionContainers.TryGetSolution(sourceUid, solutionName, out soln, out solution);

        return _solutionContainers.TryGetDrawableSolution(sourceUid, out soln, out solution);
    }

    /// <summary>
    /// Builds the injection <see cref="Solution"/> for one tick, clamping each
    /// reagent to what is actually available in the source (when present) and
    /// to the remaining capacity of the target.
    /// </summary>
    private static Solution BuildInjection(
        ConstantReagentFeedComponent comp,
        float totalSeconds,
        FixedPoint2 availableVolume,
        Solution? sourceSolution)
    {
        var solution = new Solution
        {
            MaxVolume = availableVolume
        };

        foreach (var (reagentId, ratePerSecond) in comp.ReagentsPerSecond)
        {
            if (solution.AvailableVolume <= FixedPoint2.Zero)
                break;

            var amount = ratePerSecond * totalSeconds;
            if (amount <= FixedPoint2.Zero)
                continue;

            // Clamp to what the source actually contains (when pulling).
            if (sourceSolution != null)
            {
                var sourceAvailable = sourceSolution.GetReagentQuantity(new ReagentId(reagentId, null));
                amount = FixedPoint2.Min(amount, sourceAvailable);
            }

            amount = FixedPoint2.Min(amount, solution.AvailableVolume);
            if (amount <= FixedPoint2.Zero)
                continue;

            solution.AddReagent(reagentId, amount);
        }

        return solution;
    }

    /// <summary>
    /// Removes from the source solution exactly the reagents/quantities
    /// present in the injection payload.
    /// </summary>
    private void DrainSourceReagents(Entity<SolutionComponent> sourceSoln, Solution injection)
    {
        foreach (var reagent in injection.Contents)
        {
            _solutionContainers.RemoveReagent(sourceSoln, reagent.Reagent, reagent.Quantity);
        }
    }
}
