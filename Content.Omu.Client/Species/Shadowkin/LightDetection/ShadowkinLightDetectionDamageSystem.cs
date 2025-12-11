using Content.Client.Alerts;
using Content.Client.UserInterface.Systems.Alerts.Controls;
using Content.Omu.Shared.Species.Shadowkin.LightDetection.Systems;
using Robust.Client.GameObjects;

namespace Content.Omu.Client.Species.Shadowkin.LightDetection;

public sealed class ShadowkinLightDetectionDamageSystem : SharedShadowkinLightDetectionDamageSystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Shared.Species.Shadowkin.LightDetection.Components.ShadowkinLightDetectionDamageComponent, UpdateAlertSpriteEvent>(OnUpdateAlert);
    }

    private void OnUpdateAlert(Entity<Shared.Species.Shadowkin.LightDetection.Components.ShadowkinLightDetectionDamageComponent> ent, ref UpdateAlertSpriteEvent args)
    {
        if (args.Alert.ID != ent.Comp.AlertProto)
            return;

        var alert = args.SpriteViewEnt;
        var normalized = (int)( (ent.Comp.DetectionValue / ent.Comp.DetectionValueMax) * ent.Comp.AlertMaxSeverity);
        normalized = Math.Clamp(normalized, 0, ent.Comp.AlertMaxSeverity);

        _sprite.LayerSetRsiState((alert.Owner, alert.Comp), AlertVisualLayers.Base, $"{normalized}");
    }
}
