using Content.Omu.Shared.Species.Shadowkin.LightDetection.Components;
using Content.Shared.Alert;

namespace Content.Omu.Shared.Species.Shadowkin.LightDetection.Systems;

public abstract class SharedShadowkinLightDetectionDamageSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShadowkinLightDetectionDamageComponent, MapInitEvent>(OnStartup);
        SubscribeLocalEvent<ShadowkinLightDetectionDamageComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(EntityUid uid, ShadowkinLightDetectionDamageComponent component, MapInitEvent args)
    {
        if (component.ShowAlert)
            _alerts.ShowAlert(uid, component.AlertProto);

        component.DetectionValue = component.DetectionValueMax;
    }

    private void OnShutdown(EntityUid uid, ShadowkinLightDetectionDamageComponent component, ComponentShutdown args)
    {
        _alerts.ClearAlert(uid, component.AlertProto);
    }

    public void AddResistance(Entity<ShadowkinLightDetectionDamageComponent> ent, float amount)
    {
        ent.Comp.ResistanceModifier += amount;
        DirtyField(ent.Owner, ent.Comp, nameof(ShadowkinLightDetectionDamageComponent.ResistanceModifier));
    }
}
