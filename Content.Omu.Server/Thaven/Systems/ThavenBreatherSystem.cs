using System.Diagnostics.CodeAnalysis;
using Content.Goobstation.Shared.Body;
using Content.Omu.Server.Thaven.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Drunk;
using Robust.Shared.Prototypes;

namespace Content.Omu.Server.Thaven.Systems;

public sealed class ThavenBreatherSystem : EntitySystem
{
    private static readonly ProtoId<SpeciesPrototype> ThavenSpecies = "Thaven";

    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly RespiratorSystem _respirator = default!;
    [Dependency] private readonly SharedDrunkSystem _drunk = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BodyComponent, CanMetabolizeGasEvent>(OnCanMetabolizeGas);
        SubscribeLocalEvent<BodyComponent, InhaledGasEvent>(OnInhaledGas);
        SubscribeLocalEvent<BodyComponent, CheckNeedsAirEvent>(OnCheckNeedsAir);
    }

    private void OnCanMetabolizeGas(Entity<BodyComponent> ent, ref CanMetabolizeGasEvent args)
    {
        if (!TryGetActiveThavenLung(ent, out var lung))
            return;

        args.Handled = true;
        args.Toxic = false;
        args.Saturation = args.Gas.Pressure >= lung.Comp1.MinPressure
            ? lung.Comp1.SaturationPerBreath
            : 0f;
    }

    private void OnInhaledGas(Entity<BodyComponent> ent, ref InhaledGasEvent args)
    {
        if (!TryGetActiveThavenLung(ent, out var lung))
            return;

        if (!TryComp<RespiratorComponent>(ent, out var respirator))
            return;

        // Thaven respiration depends on pressure and breathing motion, not pulmonary gas storage.
        var discardedGas = new GasMixture(args.Gas.Volume);
        _respirator.RemoveGasFromBody(ent, discardedGas);

        if (args.Gas.Pressure >= lung.Comp1.MinPressure)
            _respirator.UpdateSaturation(ent.Owner, lung.Comp1.SaturationPerBreath, respirator);

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

    private void OnCheckNeedsAir(Entity<BodyComponent> ent, ref CheckNeedsAirEvent args)
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
        Entity<BodyComponent> ent,
        [NotNullWhen(true)] out Entity<ThavenBreatherComponent, OrganComponent> lung)
    {
        lung = default;

        if (!TryComp<HumanoidAppearanceComponent>(ent, out var humanoid) || humanoid.Species != ThavenSpecies)
            return false;

        if (!_body.TryGetBodyOrganEntityComps<ThavenBreatherComponent>((ent.Owner, ent.Comp), out var lungs))
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
