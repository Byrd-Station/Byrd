// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Content.Server._Omu.Saboteur;

/// <summary>
/// Well-known domain flags used by <see cref="Components.SaboteurDirtyTrackingComponent"/>
/// to flag which objective condition categories need re-evaluation.
/// Each flag corresponds to a game-state domain that, when changed,
/// invalidates cached progress for interested objectives.
/// </summary>
[Flags]
public enum SaboteurDirtyDomain : uint
{
    /// <summary>
    /// No domain — used as the default / zero value.
    /// </summary>
    None              = 0,

    /// <summary>
    /// Triggered by <c>PowerChangedEvent</c> on <c>ApcPowerReceiverComponent</c>.
    /// Covers unpowered-equipment and APC-disabled objectives.
    /// </summary>
    Power             = 1 << 0,

    /// <summary>
    /// Triggered when a <c>SurveillanceCameraComponent</c> is deactivated.
    /// </summary>
    Camera            = 1 << 1,

    /// <summary>
    /// Triggered by <c>DoorBoltsChangedEvent</c> on <c>DoorBoltComponent</c>.
    /// </summary>
    Bolts             = 1 << 2,

    /// <summary>
    /// Triggered by station-record modifications, removals, or creations
    /// (<c>RecordModifiedEvent</c>, <c>RecordRemovedEvent</c>, <c>AfterGeneralRecordCreatedEvent</c>).
    /// </summary>
    Records           = 1 << 3,

    /// <summary>
    /// Triggered by <c>BankBalanceUpdatedEvent</c> on <c>StationBankAccountComponent</c>.
    /// </summary>
    Bank              = 1 << 4,

    /// <summary>
    /// Triggered when <c>MindcontrolledComponent</c> is added or removed from an entity.
    /// </summary>
    MindControl       = 1 << 5,

    /// <summary>
    /// Triggered by <c>ImplantImplantedEvent</c> or <c>ImplantRemovedEvent</c>
    /// on <c>SubdermalImplantComponent</c>.
    /// </summary>
    Implant           = 1 << 6,

    /// <summary>
    /// Triggered by ID-card lifecycle events (startup, shutdown, rename)
    /// and <c>AccessComponent</c> changes.
    /// </summary>
    IdCard            = 1 << 7,

    /// <summary>
    /// Triggered when a <c>SiliconLawProviderComponent</c> is added or removed.
    /// </summary>
    SiliconLaw        = 1 << 8,

    /// <summary>
    /// Triggered when a <c>FakeMindShieldComponent</c> is added or removed.
    /// </summary>
    FakeMindShield    = 1 << 9,

    /// <summary>
    /// Triggered by <c>EncryptionChannelsChangedEvent</c> on
    /// <c>EncryptionKeyHolderComponent</c>.
    /// </summary>
    EncryptionKeyHolder = 1 << 10,

    /// <summary>
    /// Triggered when certain important entities are terminated
    /// (gravity generators, cargo consoles, encryption keys, etc.).
    /// </summary>
    Entity            = 1 << 11,

    /// <summary>
    /// Triggered by <c>CommunicationConsoleAnnouncementEvent</c> when a
    /// saboteur makes a station-wide announcement.
    /// </summary>
    Announcement      = 1 << 12,

    /// <summary>
    /// Triggered by <c>GotEmaggedEvent</c> on <c>BorgChassisComponent</c>.
    /// </summary>
    Emag              = 1 << 13,
}
