using Content.Server.Mobs;
using Content.Shared._Omu.Traits;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._Omu.Traits;

public sealed class NoCritSystem : EntitySystem
{
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly DeathgaspSystem _deathgasp = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NoCritComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NoCritComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    // Core of the NoCrit system, forces the entity to skip the Crit state
    private void OnStartup(EntityUid uid, NoCritComponent component, ComponentStartup args)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        // Sets the thresholds for each species to be appropriate for what they would be before Crit state
        var maxHp = 100;
        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) &&
            (humanoid.Species == "Avali" || humanoid.Species == "Resomi"))
        {
            maxHp = 90;
        }

        _thresholds.SetMobStateThreshold(uid, maxHp, MobState.Critical, thresholds);
        _thresholds.SetMobStateThreshold(uid, maxHp, MobState.Dead, thresholds);
    }

    // Allows for the entity to deathgasp when entering into the death state instantly
    private void OnMobStateChanged(EntityUid uid, NoCritComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            _deathgasp.Deathgasp(uid);
        }
    }
}
