using System;
using Content.Shared.Stunnable;
using Robust.Shared.Random;
using Robust.Shared.Physics;
using Robust.Shared.Timing;

namespace Content.Shared.Tripping;

public sealed class TripperSystem : EntitySystem
{
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TripperComponent, MoveEvent>(OnMove);
    }

    private void OnMove(EntityUid uid, TripperComponent comp, ref MoveEvent args)
    {
        // ignore if no movement
        var distance = (args.NewPosition.Position - args.OldPosition.Position).Length();
        if (distance <= comp.MinDistance)
            return;

        // still immune from last trip?
        if (_timing.CurTime < comp.NextTripAllowed)
            return;

        var chance = comp.Chance * distance;
        if (!_random.Prob(chance))
            return;

        // Attempt to knockdown
        _stun.TryKnockdown(uid, TimeSpan.FromSeconds(comp.StunDuration), true);

        // Set immunity cooldown if configured
        if (comp.ImmunitySeconds > 0)
            comp.NextTripAllowed = _timing.CurTime + TimeSpan.FromSeconds(comp.ImmunitySeconds);
    }
}
