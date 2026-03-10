// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Server.Codewords;
using Content.Shared._Omu.Saboteur;
using Content.Shared.Random;
using Content.Shared.Security;
using Content.Shared.Store;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

using Content.Server._Omu.Saboteur.Systems;

namespace Content.Server._Omu.Saboteur.Components;

/// <summary>
/// Game rule component that configures the saboteur antag - tier thresholds,
/// TC rewards, exposure penalties, store categories, and timing intervals.
/// </summary>
[RegisterComponent, Access(typeof(SaboteurRuleSystem), typeof(SaboteurConditionCoreSystem))]
public sealed partial class SaboteurRuleComponent : Component
{
    /// <summary>
    /// Per-tier configuration: threshold, loc key, minor/major TC rewards.
    /// Index = tier number.
    /// </summary>
    [DataField(required: true)]
    public List<SaboteurTierConfig> Tiers = new();

    /// <summary>
    /// Minimum round duration before high-tier objectives can be assigned.
    /// </summary>
    [DataField(required: true)]
    public TimeSpan HighTierTimeGate;

    /// <summary>
    /// Maps criminal record status to a reputation gain multiplier (1.0 = no penalty).
    /// </summary>
    [DataField(required: true)]
    public Dictionary<SecurityStatus, float> ExposureMultipliers = new();

    /// <summary>
    /// Which codeword faction saboteurs share (typically Traitor).
    /// </summary>
    [DataField(required: true)]
    public ProtoId<CodewordFactionPrototype> CodewordFaction;

    /// <summary>
    /// Colour used to display codewords in the character briefing.
    /// </summary>
    [DataField(required: true)]
    public Color CodewordColor;

    /// <summary>
    /// All store categories that may appear in the saboteur uplink.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<StoreCategoryPrototype>> StoreCategories = new();

    /// <summary>
    /// Maps each store category to the tier at which it unlocks.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<StoreCategoryPrototype>, int> CategoryTierMap = new();

    /// <summary>
    /// Currency prototype used for telecrystals in the saboteur store.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<CurrencyPrototype> TelecrystalCurrency;

    /// <summary>
    /// How many TC the saboteur starts with (typically 0 - earned via operations).
    /// </summary>
    [DataField(required: true)]
    public int StartingTc;

    /// <summary>
    /// Catch-up reputation granted per elapsed minute for late-joining saboteurs.
    /// </summary>
    [DataField(required: true)]
    public int LateJoinRepPerMinute;

    /// <summary>
    /// Maximum catch-up reputation a late-joining saboteur can receive.
    /// </summary>
    [DataField(required: true)]
    public int MaxLateJoinRep;

    /// <summary>
    /// Catch-up TC granted per completed interval for late-joining saboteurs.
    /// </summary>
    [DataField(required: true)]
    public int LateJoinTcPerInterval;

    /// <summary>
    /// How many minutes each late-join TC interval lasts.
    /// </summary>
    [DataField(required: true)]
    public int LateJoinTcIntervalMinutes;

    /// <summary>
    /// Minimum elapsed round minutes before late-join catch-up kicks in.
    /// </summary>
    [DataField(required: true)]
    public int LateJoinMinMinutes;

    /// <summary>
    /// Hard floor for the exposure reputation multiplier - exposure can never reduce it below this.
    /// </summary>
    [DataField(required: true)]
    public float ExposureFloorMultiplier;

    /// <summary>
    /// Default reputation gain when an operation does not specify its own.
    /// </summary>
    [DataField(required: true)]
    public int DefaultOperationRepGain;

    /// <summary>
    /// Weighted random group used to assign fallback (traitor-style) objectives
    /// when all saboteur-specific objectives are exhausted.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<WeightedRandomPrototype> TraitorFallbackGroup;

    /// <summary>
    /// Entity prototype ID for the "Die a Glorious Death" last-resort objective.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId GloriousDeathObjectiveId;

    /// <summary>
    /// TC awarded per unit of difficulty when a fallback (traitor-style) objective is completed.
    /// The total TC reward is <c>floor(Difficulty × FallbackTcPerDifficulty)</c>.
    /// </summary>
    [DataField(required: true)]
    public float FallbackTcPerDifficulty;

    /// <summary>
    /// Reputation awarded per unit of difficulty when a fallback objective is completed.
    /// The total reputation gain is <c>floor(Difficulty × FallbackRepPerDifficulty)</c>.
    /// </summary>
    [DataField(required: true)]
    public float FallbackRepPerDifficulty;

    /// <summary>
    /// Maximum number of saboteur objectives a player may have active simultaneously.
    /// </summary>
    [DataField(required: true)]
    public int MaxActiveObjectives;

    /// <summary>
    /// Minimum tier number considered "high-tier" for the purposes of the time gate.
    /// </summary>
    [DataField(required: true)]
    public int HighTierMinimum;
}
