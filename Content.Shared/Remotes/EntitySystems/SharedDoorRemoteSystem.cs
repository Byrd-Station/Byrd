using Content.Shared.Access.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Electrocution;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Interaction.Events;
using Content.Shared.Remotes.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared.Remotes.EntitySystems;

public abstract class SharedDoorRemoteSystem : EntitySystem
{
    [Dependency] private readonly SharedAirlockSystem _airlock = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoorSystem _doorSystem = default!;
    [Dependency] private readonly SharedElectrocutionSystem _electrify = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiver = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] protected readonly IGameTiming Timing = default!;


    public override void Initialize()
    {
        SubscribeLocalEvent<DoorRemoteComponent, UseInHandEvent>(OnInHandActivation);
    }

    private void OnInHandActivation(Entity<DoorRemoteComponent> entity, ref UseInHandEvent args)
    {
        string switchMessageId;
        switch (entity.Comp.Mode)
        {
            case OperatingMode.OpenClose:
                entity.Comp.Mode = OperatingMode.ToggleBolts;
                switchMessageId = "door-remote-switch-state-toggle-bolts";
                break;

            // Skip toggle bolts mode and move on from there (to emergency access)
            case OperatingMode.ToggleBolts:
                entity.Comp.Mode = OperatingMode.ToggleEmergencyAccess;
                switchMessageId = "door-remote-switch-state-toggle-emergency-access";
                break;

            // Skip ToggleEmergencyAccess mode and move on from there (to door toggle)
            case OperatingMode.ToggleEmergencyAccess:
                if (airlockComp != null)
                {
                    _airlock.SetEmergencyAccess((args.Target.Value, airlockComp), !airlockComp.EmergencyAccess, user: args.User, predicted: true);
                    _adminLogger.Add(LogType.Action,
                        LogImpact.Medium,
                        $"{ToPrettyString(args.User):player} used {ToPrettyString(args.Used)} on {ToPrettyString(args.Target.Value)} to set emergency access {(airlockComp.EmergencyAccess ? "on" : "off")}");
                }

                break;
            case OperatingMode.ToggleOvercharge:
                if (TryComp<ElectrifiedComponent>(args.Target, out var eletrifiedComp))
                {
                    _electrify.SetElectrified((args.Target.Value, eletrifiedComp), !eletrifiedComp.Enabled);
                    var soundToPlay = eletrifiedComp.Enabled
                        ? eletrifiedComp.AirlockElectrifyDisabled
                        : eletrifiedComp.AirlockElectrifyEnabled;
                    _audio.PlayLocal(soundToPlay, args.Target.Value, args.User);
                    _adminLogger.Add(LogType.Action,
                        LogImpact.Medium,
                        $"{ToPrettyString(args.User):player} used {ToPrettyString(args.Used)} on {ToPrettyString(args.Target.Value)} to {(eletrifiedComp.Enabled ? "" : "un")}electrify it");
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"{nameof(DoorRemoteComponent)} had invalid mode {entity.Comp.Mode}");
        }
        Dirty(entity);
        Popup.PopupClient(Loc.GetString(switchMessageId), entity, args.User);
    }
}
