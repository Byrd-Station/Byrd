using Content.Omu.Server.Thaven.Components;
using Content.Omu.Shared.Thaven.Components;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Goobstation.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Atmos;
using Content.Shared.Drunk;

namespace Content.Omu.Server.Thaven.Systems;

public sealed class ThavenBreatherSystem : EntitySystem
{
    [Dependency] private readonly RespiratorSystem _respirator = default!;
    [Dependency] private readonly SharedDrunkSystem _drunk = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ThavenBreatherComponent, CanMetabolizeGasEvent>(OnCanMetabolizeGas);
        SubscribeLocalEvent<ThavenBreatherComponent, InhaledGasEvent>(OnInhaledGas);
        SubscribeLocalEvent<ThavenBreatherComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<ThavenBreatherComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);
    }

    /// <summary>
    /// When Thaven lungs are installed in a body, propagate ThavenBreatherComponent to the body.
    /// Only valid for actual Thavens — prevents non-Thavens from benefiting from stolen lungs.
    /// </summary>
    private void OnOrganAdded(Entity<ThavenBreatherComponent> organ, ref OrganAddedToBodyEvent args)
    {
        if (!HasComp<ThavenMoodsComponent>(args.Body))
            return;

        var comp = EnsureComp<ThavenBreatherComponent>(args.Body);
        comp.MinPressure = organ.Comp.MinPressure;
        comp.SaturationPerBreath = organ.Comp.SaturationPerBreath;
        comp.IntoxicatingGas = organ.Comp.IntoxicatingGas;
        comp.IntoxicatingGasMinRatio = organ.Comp.IntoxicatingGasMinRatio;
        comp.IntoxicatingGasDurationScale = organ.Comp.IntoxicatingGasDurationScale;
        comp.IntoxicatingGasMinDuration = organ.Comp.IntoxicatingGasMinDuration;
        comp.IntoxicatingGasMaxDuration = organ.Comp.IntoxicatingGasMaxDuration;
        comp.IntoxicatingGasApplySlur = organ.Comp.IntoxicatingGasApplySlur;
    }

    /// <summary>
    /// When Thaven lungs are removed, remove ThavenBreatherComponent from the body.
    /// </summary>
    private void OnOrganRemoved(Entity<ThavenBreatherComponent> organ, ref OrganRemovedFromBodyEvent args)
    {
        RemComp<ThavenBreatherComponent>(args.OldBody);
    }

    private void OnCanMetabolizeGas(Entity<ThavenBreatherComponent> ent, ref CanMetabolizeGasEvent args)
    {
        args.Handled = true;
        args.Toxic = false;
        args.Saturation = args.Gas.Pressure >= ent.Comp.MinPressure
            ? ent.Comp.SaturationPerBreath
            : 0f;
    }

    private void OnInhaledGas(Entity<ThavenBreatherComponent> ent, ref InhaledGasEvent args)
    {
        if (!TryComp<RespiratorComponent>(ent, out var respirator))
            return;

        // BodyComponent may have already routed the inhaled mix into lung storage before this handler runs.
        // Thaven should derive respiration from motion/pressure, not pulmonary gas metabolism, so purge it.
        if (TryComp<BodyComponent>(ent, out var body))
        {
            var discardedGas = new GasMixture(args.Gas.Volume);
            _respirator.RemoveGasFromBody((ent.Owner, body), discardedGas);
        }

        // Thaven derive respiration from the act of breathing itself rather than metabolizing specific gases.
        if (args.Gas.Pressure >= ent.Comp.MinPressure)
            _respirator.UpdateSaturation(ent, ent.Comp.SaturationPerBreath, respirator);

        // Frezon leaves thaven mildly intoxicated instead of poisoning them.
        var totalMoles = args.Gas.TotalMoles;
        if (totalMoles > 0f)
        {
            var intoxicatingGasRatio = args.Gas.GetMoles(ent.Comp.IntoxicatingGas) / totalMoles;
            if (intoxicatingGasRatio >= ent.Comp.IntoxicatingGasMinRatio)
            {
                var drunkenness = Math.Max(
                    intoxicatingGasRatio * ent.Comp.IntoxicatingGasDurationScale,
                    ent.Comp.IntoxicatingGasMinDuration);

                if (ent.Comp.IntoxicatingGasMaxDuration > 0f)
                    drunkenness = Math.Min(drunkenness, ent.Comp.IntoxicatingGasMaxDuration);

                _drunk.TryApplyDrunkenness(ent.Owner, drunkenness, ent.Comp.IntoxicatingGasApplySlur);
            }
        }

        // Prevent merge-back and treat breathing as handled through the custom Thaven path.
        args.Handled = true;
        args.Succeeded = true;
    }
}

