using System.Diagnostics.CodeAnalysis;
using Content.Goobstation.Shared.Body;
using Content.Server._Impstation.Thaven.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Drunk;

namespace Content.Server._Impstation.Thaven.Systems;

/// <summary>
/// Handles breathing for any entity that has Thaven lungs installed.
/// The organ (ThavenBreatherComponent) is the source of all configuration.
/// Events are intercepted at the body level before RespiratorSystem processes them.
/// </summary>
public sealed class ThavenBreatherSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly RespiratorSystem _respirator = default!;
    [Dependency] private readonly SharedDrunkSystem _drunk = default!;

    public override void Initialize()
    {
        base.Initialize();
        var before = new[] { typeof(RespiratorSystem) };
        SubscribeLocalEvent<RespiratorComponent, CanMetabolizeGasEvent>(OnCanMetabolizeGas, before: before);
        SubscribeLocalEvent<RespiratorComponent, InhaledGasEvent>(OnInhaledGas, before: before);
        SubscribeLocalEvent<RespiratorComponent, CheckNeedsAirEvent>(OnCheckNeedsAir, before: before);
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

        // Always handle so normal lung gas chemistry does not run.
        args.Handled = true;

        // Thaven respiration depends on pressure and breathing motion, not pulmonary gas storage.
        var discardedGas = new GasMixture(args.Gas.Volume);
        _respirator.RemoveGasFromBody((ent.Owner, body), discardedGas);

        if (args.Gas.Pressure < lung.Comp1.MinPressure)
            return; // Not enough pressure - breathing fails, suffocation applies normally.

        _respirator.UpdateSaturation(ent.Owner, lung.Comp1.SaturationPerBreath, ent.Comp);
        args.Succeeded = true;

        var totalMoles = args.Gas.TotalMoles;
        if (totalMoles <= 0f)
            return;

        var intoxicatingGasRatio = args.Gas.GetMoles(lung.Comp1.IntoxicatingGas) / totalMoles;
        if (intoxicatingGasRatio < lung.Comp1.IntoxicatingGasMinRatio)
            return;

        var drunkenness = Math.Max(
            intoxicatingGasRatio * lung.Comp1.IntoxicatingGasDurationScale,
            lung.Comp1.IntoxicatingGasMinDuration);

        if (lung.Comp1.IntoxicatingGasMaxDuration > 0f)
            drunkenness = Math.Min(drunkenness, lung.Comp1.IntoxicatingGasMaxDuration);

        _drunk.TryApplyDrunkenness(ent.Owner, drunkenness, lung.Comp1.IntoxicatingGasApplySlur);
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

        // No adequate atmosphere - drain saturation so normal suffocation kicks in.
        // Skip the needs-air check to avoid recursively re-raising the same event.
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
