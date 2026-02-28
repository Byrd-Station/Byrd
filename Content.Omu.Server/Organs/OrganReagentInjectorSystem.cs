// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Omu.Shared.Organs;
using Content.Shared.Body.Organ;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Omu.Server.Organs;

/// <summary>
/// Injects reagents from organs into their host body's solutions at regular intervals.
/// </summary>
public sealed class OrganReagentInjectorSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<OrganReagentInjectorComponent, OrganComponent>();
        while (query.MoveNext(out var uid, out var injector, out var organ))
        {
            if (_timing.CurTime < injector.NextInjectTime)
                continue;

            injector.NextInjectTime = _timing.CurTime + injector.Duration;

            if (organ.Body is not { } body)
                continue;

            if (_solution.TryGetSolution(body, injector.TargetSolution, out var soln, out _))
                _solution.TryAddSolution(soln.Value, injector.Reagents.Clone());
        }
    }
}
