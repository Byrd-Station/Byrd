using Content.Goobstation.Shared.Body;
using Content.Omu.Server.Thaven.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;

namespace Content.Omu.Server.Thaven;

/// <summary>
/// Hooks Thaven pressure-based breathing into the respirator without modifying the upstream base file.
///
/// The upstream <see cref="RespiratorSystem.CanBreathe"/> previously contained two Thaven-specific blocks:
///   1. A TryComp&lt;ThavenBreatherComponent&gt; block that bypassed saturation entirely and returned
///      based on atmospheric pressure alone. This is replicated here by subscribing to
///      <see cref="CheckNeedsAirEvent"/> and cancelling it (= "can always breathe") when pressure is adequate.
///      When pressure is too low or there is no atmosphere (vacuum/space), saturation is actively drained
///      so normal suffocation applies.
///   2. A !HasComp&lt;ThavenBreatherComponent&gt; guard that suppressed the gasp emote while suffocating.
///      No upstream event exists at that call site, so this suppression is intentionally not reproduced
///      here — Thavens will gasp when suffocating in vacuum, which is acceptable behaviour.
/// </summary>
public sealed class ThavenRespiratorSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosSys = default!;
    [Dependency] private readonly RespiratorSystem _respirator = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ThavenBreatherComponent, CheckNeedsAirEvent>(OnCheckNeedsAir);
    }

    /// <summary>
    /// Thavens breathe from pressure and motion rather than a specific gas.
    /// Cancel the CheckNeedsAirEvent when pressure is sufficient so CanBreathe returns true.
    /// When in vacuum or below MinPressure, saturation is drained directly so suffocation
    /// applies even when there is no gas to metabolize (e.g. open space).
    /// </summary>
    private void OnCheckNeedsAir(Entity<ThavenBreatherComponent> ent, ref CheckNeedsAirEvent args)
    {
        var mixture = _atmosSys.GetContainingMixture(ent.Owner, excite: true);
        if (mixture != null && mixture.Pressure >= ent.Comp.MinPressure)
        {
            args.Cancelled = true;
            return;
        }

        // No adequate atmosphere — drain saturation so normal suffocation kicks in.
        // Without this, saturation stays high in vacuum because no gas is ever metabolized.
        _respirator.UpdateSaturation(ent.Owner, -ent.Comp.SaturationPerBreath);
    }
}
