using Content.Server._Omu.Thaven.Components;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Goobstation.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Atmos;

namespace Content.Server._Omu.Thaven.Systems;

public sealed class ThavenBreatherSystem : EntitySystem
{
    [Dependency] private readonly RespiratorSystem _respirator = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ThavenBreatherComponent, CanMetabolizeGasEvent>(OnCanMetabolizeGas);
        SubscribeLocalEvent<ThavenBreatherComponent, InhaledGasEvent>(OnInhaledGas);
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
        if (TryComp<BodyComponent>(ent, out var body) && args.Succeeded)
        {
            var discardedGas = new GasMixture(args.Gas.Volume);
            _respirator.RemoveGasFromBody((ent.Owner, body), discardedGas);
        }

        // Thaven derive respiration from the act of breathing itself rather than metabolizing specific gases.
        if (args.Gas.Pressure >= ent.Comp.MinPressure)
            _respirator.UpdateSaturation(ent, ent.Comp.SaturationPerBreath, respirator);

        // Prevent merge-back and treat breathing as handled through the custom Thaven path.
        args.Handled = true;
        args.Succeeded = true;
    }
}

