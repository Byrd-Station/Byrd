using Content.Client.Alerts;
using Content.Client.UserInterface.Systems.Alerts.Controls;
using Content.Omu.Shared.ShadowkinLightDetection.Components;
using Content.Omu.Shared.ShadowkinLightDetection.Systems;
using Robust.Client.GameObjects;

namespace Content.Omu.Client.ShadowkinLightDetection;

public sealed class ShadowkinLightDetectionDamageSystem : SharedShadowkinLightDetectionDamageSystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShadowkinLightDetectionDamageComponent, UpdateAlertSpriteEvent>(OnUpdateAlert);
    }

    private void OnUpdateAlert(Entity<ShadowkinLightDetectionDamageComponent> ent, ref UpdateAlertSpriteEvent args)
    {
        if (args.Alert.ID != ent.Comp.AlertProto)
            return;

        var alert = args.SpriteViewEnt;
        var normalized = (int)( (ent.Comp.DetectionValue / ent.Comp.DetectionValueMax) * ent.Comp.AlertMaxSeverity);
        normalized = Math.Clamp(normalized, 0, ent.Comp.AlertMaxSeverity);

        _sprite.LayerSetRsiState((alert.Owner, alert.Comp), AlertVisualLayers.Base, $"{normalized}");
    }
}
