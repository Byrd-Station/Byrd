// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Shared._Omu.Saboteur;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Omu.Saboteur;

/// <summary>
/// Raised on the rule entity when a saboteur completes an operation.
/// </summary>
[ByRefEvent]
public readonly record struct SaboteurOperationCompletedEvent(
    EntityUid Mind,
    ProtoId<SaboteurOperationPrototype> OperationId,
    int Tier,
    int ReputationGained);

/// <summary>
/// Raised on the rule entity when a saboteur advances to a higher tier.
/// </summary>
[ByRefEvent]
public readonly record struct SaboteurTierAdvancedEvent(
    EntityUid Mind,
    int OldTier,
    int NewTier);

/// <summary>
/// Raised on the rule entity when a saboteur's reputation changes.
/// </summary>
[ByRefEvent]
public readonly record struct SaboteurReputationChangedEvent(
    EntityUid Mind,
    int OldReputation,
    int NewReputation);

/// <summary>
/// Raised on the rule entity when a saboteur's exposure penalty changes.
/// </summary>
[ByRefEvent]
public readonly record struct SaboteurExposureUpdatedEvent(
    EntityUid Mind,
    float OldMultiplier,
    float NewMultiplier);

/// <summary>
/// Broadcast when a saboteur makes a station announcement via a communications console.
/// Condition systems can subscribe to perform condition-specific tracking.
/// </summary>
[ByRefEvent]
public readonly record struct SaboteurAnnouncementMadeEvent(
    EntityUid Sender,
    MindComponent Mind,
    EntityUid ConsoleUid);

/// <summary>
/// Broadcast when a saboteur emags a borg.
/// Condition systems can subscribe to perform condition-specific tracking.
/// </summary>
[ByRefEvent]
public readonly record struct SaboteurBorgEmaggedByAgentEvent(
    MindComponent Mind,
    EntityUid BorgUid);
