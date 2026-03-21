using Content.Server.Explosion.EntitySystems;
using Content.Server.Procedural.Components;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;

namespace Content.Server.Procedural.EntitySystems;

public sealed class ScrapShipDoorTrapSystem : EntitySystem
{
    [Dependency] private readonly ExplosionSystem _explosion = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ScrapShipDoorTrapComponent, DoorStateChangedEvent>(OnDoorStateChanged);
    }

    private void OnDoorStateChanged(Entity<ScrapShipDoorTrapComponent> ent, ref DoorStateChangedEvent args)
    {
        if (ent.Comp.Triggered)
            return;

        if (args.State != DoorState.Opening && args.State != DoorState.Open)
            return;

        ent.Comp.Triggered = true;
        _explosion.QueueExplosion(
            ent.Owner,
            ent.Comp.ExplosionType,
            ent.Comp.TotalIntensity,
            ent.Comp.Slope,
            ent.Comp.MaxTileIntensity,
            canCreateVacuum: ent.Comp.CanCreateVacuum);
    }
}
