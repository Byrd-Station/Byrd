using Content.Omu.Shared.Species.Shadowkin.LightDetection.Systems;
using Content.Shared._Shitmed.Damage;
using Content.Shared._Shitmed.Medical.Surgery.Wounds.Systems;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Omu.Server.Species.Shadowkin.LightDetection;

/// <summary>
/// This handles healing or dealing damage to an entity that is standing on a lighted area.
/// </summary>
public sealed class ShadowkinLightDetectionDamageSystem : SharedShadowkinLightDetectionDamageSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly WoundSystem _woundSystem = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<Shared.Species.Shadowkin.LightDetection.Components.ShadowkinLightDetectionDamageComponent, Shared.Species.Shadowkin.LightDetection.Components.ShadowkinLightDetectionComponent>();
        while (query.MoveNext(out var uid, out var comp, out var lightDet))
        {
            if (comp.NextUpdate > _timing.CurTime)
                return;

            comp.NextUpdate = _timing.CurTime + comp.UpdateInterval;

            UpdateDetectionValues(comp, lightDet.CurrentLightLevel);
            DirtyField(uid, comp, nameof(Shared.Species.Shadowkin.LightDetection.Components.ShadowkinLightDetectionDamageComponent.DetectionValue));

            switch (comp.DetectionValue)
            {
                case <= 0 when comp.TakeDamageOnLight && !_mobState.IsDead(uid):
                    _damageable.TryChangeDamage(uid, comp.DamageToDeal * comp.ResistanceModifier, splitDamage: SplitDamageBehavior.SplitEnsureAll);
                    _audio.PlayPvs(comp.SoundOnDamage, uid, AudioParams.Default.WithVolume(-2f));
                    return;
                case >= 80 when comp.HealOnShadows && !_mobState.IsDead(uid):
                    _woundSystem.TryHealWoundsOnOwner(uid, comp.DamageToHeal, true);
                    _damageable.TryChangeDamage(uid, comp.DamageToHeal, true, false, targetPart: TargetBodyPart.All, splitDamage: SplitDamageBehavior.SplitEnsureAllOrganic, canMiss: false);
                    return;
            }
        }
    }

    private void UpdateDetectionValues(Shared.Species.Shadowkin.LightDetection.Components.ShadowkinLightDetectionDamageComponent comp, float detectionDamage)
    {
        var detectionDelta = comp.DetectionValueRegeneration - detectionDamage;
        comp.DetectionValue += detectionDelta;
        comp.DetectionValue = Math.Clamp(comp.DetectionValue, 0f, comp.DetectionValueMax);
    }
}
