using System.Diagnostics.CodeAnalysis;
using Content.Goobstation.Shared.Body;
using Content.Omu.Server.Thaven.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Drunk;

namespace Content.Omu.Server.Thaven.Systems;

public sealed class ThavenBreatherSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly RespiratorSystem _respirator = default!;
    [Dependency] private readonly SharedDrunkSystem _drunk = default!;

    public override void Initialize()
    {
        base.Initialize();
        var beforeRespirator = new[] { typeof(RespiratorSystem) };
        SubscribeLocalEvent<RespiratorComponent, CanMetabolizeGasEvent>(OnCanMetabolizeGas, before: beforeRespirator);
        SubscribeLocalEvent<RespiratorComponent, InhaledGasEvent>(OnInhaledGas, before: beforeRespirator);
        SubscribeLocalEvent<RespiratorComponent, CheckNeedsAirEvent>(OnCheckNeedsAir, before: beforeRespirator);
    }

    private void OnCanMetabolizeGas(Entity<RespiratorComponent> ent, ref CanMetabolizeGasEvent args)
    {
        if (!TryGetActiveThavenLung(ent, out var lung))
            return;

        args.Handled = true;
        args.Toxic = false;
        args.Saturation = args.Gas.Pressure >= lung.Comp1.MinPressure
            ? lung.Comp1.SaturationPerBreath
            : 0f;
    }

    private void OnInhaledGas(Entity<RespiratorComponent> ent, ref InhaledGasEvent args)
    {
        if (!TryGetActiveThavenLung(ent, out var lung))
            return;

        if (!TryComp<BodyComponent>(ent, out var body))
            return;

        // Thaven respiration depends on pressure and breathing motion, not pulmonary gas storage.
        var discardedGas = new GasMixture(args.Gas.Volume);
        _respirator.RemoveGasFromBody((ent.Owner, body), discardedGas);

        if (args.Gas.Pressure >= lung.Comp1.MinPressure)
            _respirator.UpdateSaturation(ent.Owner, lung.Comp1.SaturationPerBreath, ent.Comp);

        var totalMoles = args.Gas.TotalMoles;
        if (totalMoles > 0f)
        {
            var intoxicatingGasRatio = args.Gas.GetMoles(lung.Comp1.IntoxicatingGas) / totalMoles;
            if (intoxicatingGasRatio >= lung.Comp1.IntoxicatingGasMinRatio)
            {
                var drunkenness = Math.Max(
                    intoxicatingGasRatio * lung.Comp1.IntoxicatingGasDurationScale,
                    lung.Comp1.IntoxicatingGasMinDuration);

                if (lung.Comp1.IntoxicatingGasMaxDuration > 0f)
                    drunkenness = Math.Min(drunkenness, lung.Comp1.IntoxicatingGasMaxDuration);

                _drunk.TryApplyDrunkenness(ent.Owner, drunkenness, lung.Comp1.IntoxicatingGasApplySlur);
            }
        }

        args.Handled = true;
        args.Succeeded = true;
    }

    private void OnCheckNeedsAir(Entity<RespiratorComponent> ent, ref CheckNeedsAirEvent args)
    {
        if (!TryGetActiveThavenLung(ent, out var lung))
            return;

        var mixture = _atmosphere.GetContainingMixture(ent.Owner, excite: true);
        if (mixture != null && mixture.Pressure >= lung.Comp1.MinPressure)
        {
            args.Cancelled = true;
            return;
        }

        _respirator.UpdateSaturation(ent.Owner, -lung.Comp1.SaturationPerBreath, skipNeedsAirCheck: true);
    }

    private bool TryGetActiveThavenLung(
        EntityUid uid,
        [NotNullWhen(true)] out Entity<ThavenBreatherComponent, OrganComponent> lung)
    {
        lung = default;

        if (!TryComp<BodyComponent>(uid, out var body))
            return false;

        if (!_body.TryGetBodyOrganEntityComps<ThavenBreatherComponent>((uid, body), out var lungs))
            return false;

        foreach (var organ in lungs)
        {
            if (!organ.Comp2.Enabled)
                continue;

            lung = organ;
            return true;
        }

        return false;
    }
}
