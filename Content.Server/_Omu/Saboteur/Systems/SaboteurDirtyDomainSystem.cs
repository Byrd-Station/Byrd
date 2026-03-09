// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server.Communications;
using Content.Server.Gravity;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Server.SurveillanceCamera;
using Content.Shared.Access.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Gravity;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mind;
using Content.Shared.Mindshield.Components;
using Content.Shared.Power;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Silicons.Laws;
using Content.Shared.Silicons.Laws.Components;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.StationRecords;
using Content.Goobstation.Shared.Mindcontrol;

using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Systems;

/// <summary>
/// Listens to game-state change events (power, cameras, records, etc.) and marks
/// the corresponding dirty domain so affected saboteur objectives get re-evaluated.
/// </summary>
public sealed class SaboteurDirtyDomainSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ApcPowerReceiverComponent, PowerChangedEvent>(OnPowerChanged);

        SubscribeLocalEvent<SurveillanceCameraDeactivateEvent>(OnCameraDeactivated);

        SubscribeLocalEvent<DoorBoltComponent, DoorBoltsChangedEvent>(OnBoltsChanged);

        SubscribeLocalEvent<RecordModifiedEvent>(OnRecordModified);
        SubscribeLocalEvent<RecordRemovedEvent>(OnRecordRemoved);
        SubscribeLocalEvent<AfterGeneralRecordCreatedEvent>(OnRecordCreated);

        SubscribeLocalEvent<StationBankAccountComponent, BankBalanceUpdatedEvent>(OnBankBalanceUpdated);

        SubscribeLocalEvent<MindcontrolledComponent, ComponentAdd>(OnMindControlStarted);
        SubscribeLocalEvent<MindcontrolledComponent, ComponentRemove>(OnMindControlEnded);

        SubscribeLocalEvent<SubdermalImplantComponent, ImplantImplantedEvent>(OnImplantInserted);
        SubscribeLocalEvent<SubdermalImplantComponent, ImplantRemovedEvent>(OnImplantRemoved);

        SubscribeLocalEvent<IdCardComponent, ComponentStartup>(OnIdCardStartup);
        SubscribeLocalEvent<IdCardComponent, ComponentShutdown>(OnIdCardShutdown);
        SubscribeLocalEvent<IdCardComponent, EntityRenamedEvent>(OnIdCardRenamed);
        SubscribeLocalEvent<AccessComponent, ComponentStartup>(OnAccessStartup);
        SubscribeLocalEvent<AccessComponent, ComponentShutdown>(OnAccessShutdown);

        SubscribeLocalEvent<SiliconLawProviderComponent, ComponentStartup>(OnSiliconLawStartup);
        SubscribeLocalEvent<SiliconLawProviderComponent, ComponentShutdown>(OnSiliconLawShutdown);

        SubscribeLocalEvent<FakeMindShieldComponent, ComponentStartup>(OnFakeMindShieldStartup);
        SubscribeLocalEvent<FakeMindShieldComponent, ComponentShutdown>(OnFakeMindShieldShutdown);

        SubscribeLocalEvent<CommunicationConsoleAnnouncementEvent>(OnCommsAnnouncement);

        SubscribeLocalEvent<EncryptionKeyHolderComponent, EncryptionChannelsChangedEvent>(OnEncryptionKeysChanged);

        SubscribeLocalEvent<GravityGeneratorComponent, EntityTerminatingEvent>(OnGravGenTerminating);
        SubscribeLocalEvent<CargoOrderConsoleComponent, EntityTerminatingEvent>(OnCargoConsoleTerminating);
        SubscribeLocalEvent<EncryptionKeyComponent, EntityTerminatingEvent>(OnEncryptionKeyTerminating);

        SubscribeLocalEvent<BorgChassisComponent, GotEmaggedEvent>(OnBorgEmagged);
    }

    // Only dirty-track station entities to avoid spurious re-evaluation from shuttles/space.
    private void OnPowerChanged(EntityUid uid, ApcPowerReceiverComponent comp, ref PowerChangedEvent args)
    {
        if (_station.GetOwningStation(uid) == null)
            return;

        _core.MarkDirty(SaboteurDirtyDomain.Power);
    }

    private void OnCameraDeactivated(SurveillanceCameraDeactivateEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Camera);

    // Only dirty-track station entities to avoid spurious re-evaluation from shuttles/space.
    private void OnBoltsChanged(EntityUid uid, DoorBoltComponent comp, DoorBoltsChangedEvent args)
    {
        if (_station.GetOwningStation(uid) == null)
            return;

        _core.MarkDirty(SaboteurDirtyDomain.Bolts);
    }

    private void OnRecordModified(RecordModifiedEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Records);

    private void OnRecordRemoved(RecordRemovedEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Records);

    private void OnRecordCreated(AfterGeneralRecordCreatedEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Records);

    private void OnBankBalanceUpdated(EntityUid uid, StationBankAccountComponent comp, ref BankBalanceUpdatedEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Bank);

    private void OnMindControlStarted(EntityUid uid, MindcontrolledComponent comp, ComponentAdd args)
        => _core.MarkDirty(SaboteurDirtyDomain.MindControl);

    private void OnMindControlEnded(EntityUid uid, MindcontrolledComponent comp, ComponentRemove args)
        => _core.MarkDirty(SaboteurDirtyDomain.MindControl);

    private void OnImplantInserted(EntityUid uid, SubdermalImplantComponent comp, ref ImplantImplantedEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Implant);

    private void OnImplantRemoved(EntityUid uid, SubdermalImplantComponent comp, ref ImplantRemovedEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Implant);

    private void OnIdCardStartup(EntityUid uid, IdCardComponent comp, ComponentStartup args)
        => _core.MarkDirty(SaboteurDirtyDomain.IdCard);

    private void OnIdCardShutdown(EntityUid uid, IdCardComponent comp, ComponentShutdown args)
        => _core.MarkDirty(SaboteurDirtyDomain.IdCard);

    private void OnIdCardRenamed(EntityUid uid, IdCardComponent comp, ref EntityRenamedEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.IdCard);

    private void OnAccessStartup(EntityUid uid, AccessComponent comp, ComponentStartup args)
        => _core.MarkDirty(SaboteurDirtyDomain.IdCard);

    private void OnAccessShutdown(EntityUid uid, AccessComponent comp, ComponentShutdown args)
        => _core.MarkDirty(SaboteurDirtyDomain.IdCard);

    private void OnSiliconLawStartup(EntityUid uid, SiliconLawProviderComponent comp, ComponentStartup args)
        => _core.MarkDirty(SaboteurDirtyDomain.SiliconLaw);

    private void OnSiliconLawShutdown(EntityUid uid, SiliconLawProviderComponent comp, ComponentShutdown args)
        => _core.MarkDirty(SaboteurDirtyDomain.SiliconLaw);

    private void OnFakeMindShieldStartup(EntityUid uid, FakeMindShieldComponent comp, ComponentStartup args)
        => _core.MarkDirty(SaboteurDirtyDomain.FakeMindShield);

    private void OnFakeMindShieldShutdown(EntityUid uid, FakeMindShieldComponent comp, ComponentShutdown args)
        => _core.MarkDirty(SaboteurDirtyDomain.FakeMindShield);

    private void OnEncryptionKeysChanged(EntityUid uid, EncryptionKeyHolderComponent comp, EncryptionChannelsChangedEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.EncryptionKeyHolder);

    private void OnGravGenTerminating(EntityUid uid, GravityGeneratorComponent comp, ref EntityTerminatingEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Entity);

    private void OnCargoConsoleTerminating(EntityUid uid, CargoOrderConsoleComponent comp, ref EntityTerminatingEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Entity);

    private void OnEncryptionKeyTerminating(EntityUid uid, EncryptionKeyComponent comp, ref EntityTerminatingEvent args)
        => _core.MarkDirty(SaboteurDirtyDomain.Entity);

    private void OnCommsAnnouncement(ref CommunicationConsoleAnnouncementEvent args)
    {
        if (args.Sender is not { } sender)
            return;

        if (!HasComp<SaboteurComponent>(sender))
            return;

        if (!_mind.TryGetMind(sender, out _, out var mind))
            return;

        var ev = new SaboteurAnnouncementMadeEvent(sender, mind, args.Uid);
        RaiseLocalEvent(ref ev);
        _core.MarkDirty(SaboteurDirtyDomain.Announcement);
    }

    private void OnBorgEmagged(EntityUid uid, BorgChassisComponent comp, ref GotEmaggedEvent args)
    {
        if (!HasComp<SaboteurComponent>(args.UserUid))
            return;

        if (!_mind.TryGetMind(args.UserUid, out _, out var mind))
            return;

        var ev = new SaboteurBorgEmaggedByAgentEvent(mind, uid);
        RaiseLocalEvent(ref ev);
        _core.MarkDirty(SaboteurDirtyDomain.Emag);
    }
}
