using Content.Shared._Omu.Traits;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Humanoid;

namespace Content.Server._Omu.Traits;

public sealed class NoCritSystem : EntitySystem
{
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NoCritComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, NoCritComponent component, ComponentStartup args)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        var maxHp = 100;
        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) &&
            (humanoid.Species == "Avali" || humanoid.Species == "Resomi"))
        {
            maxHp = 90;
        }

        _thresholds.SetMobStateThreshold(uid, maxHp, MobState.Critical, thresholds);
        _thresholds.SetMobStateThreshold(uid, maxHp, MobState.Dead, thresholds);
    }
}
