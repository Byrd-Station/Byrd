using Content.Goobstation.Shared.Body;
using Content.Omu.Server.Thaven.Components;
using Content.Server.Atmos.EntitySystems;

namespace Content.Omu.Server.Thaven;

/// <summary>
/// Hooks Thaven pressure-based breathing into the respirator without modifying the upstream base file.
///
/// The upstream <see cref="RespiratorSystem.CanBreathe"/> previously contained two Thaven-specific blocks:
///   1. A TryComp&lt;ThavenBreatherComponent&gt; block that bypassed saturation entirely and returned
///      based on atmospheric pressure alone. This is replicated here by subscribing to
///      <see cref="CheckNeedsAirEvent"/> and cancelling it (= "can always breathe") when pressure is adequate.
///      When pressure is too low the event is not cancelled, saturation falls to zero (via
///      <see cref="ThavenBreatherSystem.OnCanMetabolizeGas"/>), and normal suffocation follows.
///   2. A !HasComp&lt;ThavenBreatherComponent&gt; guard that suppressed the gasp emote while suffocating.
///      No upstream event exists at that call site, so this suppression is intentionally not reproduced
///      here — Thavens will gasp when suffocating in vacuum, which is acceptable behaviour.
/// </summary>
public sealed class ThavenRespiratorSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosSys = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ThavenBreatherComponent, CheckNeedsAirEvent>(OnCheckNeedsAir);
    }

    /// <summary>
    /// Thavens breathe from pressure and motion rather than a specific gas.
    /// Cancel the CheckNeedsAirEvent when pressure is sufficient so CanBreathe returns true
    /// without relying on the gas-metabolism saturation path.
    /// When pressure is below MinPressure the event is not cancelled: saturation will fall to
    /// zero (ThavenBreatherSystem sets it to 0 on OnCanMetabolizeGas) and normal suffocation applies.
    /// </summary>
    private void OnCheckNeedsAir(Entity<ThavenBreatherComponent> ent, ref CheckNeedsAirEvent args)
    {
        var mixture = _atmosSys.GetContainingMixture(ent.Owner, excite: true);
        if (mixture != null && mixture.Pressure >= ent.Comp.MinPressure)
            args.Cancelled = true;
    }
}
