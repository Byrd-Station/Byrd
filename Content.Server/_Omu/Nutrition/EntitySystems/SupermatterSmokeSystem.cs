using Content.Server._Omu.Nutrition.Components;
using Content.Shared.Atmos;
using Content.Shared.Inventory;
using Content.Shared.Nutrition.Components;
using Content.Shared.Smoking;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Omu.Nutrition.EntitySystems;

/// <summary>
/// Handles the effects of supermatter shard smokes: periodically spawning tesla mini energy balls
/// at the smoker's position.
/// </summary>
public sealed class SupermatterSmokeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupermatterSmokeComponent, IgnitedEvent>(OnIgnited);
        SubscribeLocalEvent<SupermatterSmokeComponent, ExtinguishedEvent>(OnExtinguished);
    }

    private void OnIgnited(Entity<SupermatterSmokeComponent> ent, ref IgnitedEvent args)
    {
        ent.Comp.IsActive = true;
        ent.Comp.NextSpawnTime = _timing.CurTime + _random.Next(ent.Comp.MinSpawnInterval, ent.Comp.MaxSpawnInterval);
    }

    private void OnExtinguished(Entity<SupermatterSmokeComponent> ent, ref ExtinguishedEvent args)
    {
        ent.Comp.IsActive = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<BurningComponent, SupermatterSmokeComponent>();

        while (query.MoveNext(out var uid, out _, out var comp))
        {
            if (!comp.IsActive)
                continue;

            // Verify the smoke is actually in someone's mask slot — same pattern as SmokingSystem
            if (!_container.TryGetContainingContainer((uid, null, null), out var containerManager))
                continue;

            var smokerUid = containerManager.Owner;

            if (!_inventory.TryGetSlotEntity(smokerUid, "mask", out var maskUid) || maskUid != uid)
                continue;

            // Spawn tesla energy balls on timer
            if (curTime >= comp.NextSpawnTime)
            {
                Spawn(comp.TeslaPrototype, Transform(smokerUid).Coordinates);
                comp.NextSpawnTime = curTime + _random.Next(comp.MinSpawnInterval, comp.MaxSpawnInterval);
            }
        }
    }
}
