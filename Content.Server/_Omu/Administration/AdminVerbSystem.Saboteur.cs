// SPDX-FileCopyrightText: 2026 Eponymic-sys
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Omu.Saboteur.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.Database;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Systems;

/// <summary>
/// OmuStation partial: Saboteur admin verb.
/// </summary>
public sealed partial class AdminVerbSystem
{
    [Dependency] private readonly SaboteurRuleSystem _saboteurRule = default!;

    /// <summary>
    /// Default saboteur game rule entity prototype ID.
    /// Centralized here so admin verbs and other entry points reference a single value.
    /// </summary>
    public static readonly EntProtoId DefaultSaboteurRule = "Saboteur";

    /// <summary>
    /// Adds the "Make Saboteur" admin verb to the antag verb list.
    /// </summary>
    private void AddSaboteurAntagVerb(GetVerbsEvent<Verb> args, ICommonSession targetPlayer)
    {
        var saboteurName = Loc.GetString("admin-verb-text-make-saboteur");
        Verb saboteurVerb = new()
        {
            Text = saboteurName,
            Category = VerbCategory.Antag,
            Icon = new SpriteSpecifier.Rsi(new ResPath("/Textures/Interface/Misc/job_icons.rsi"), "Syndicate"),
            Act = () =>
            {
                // Saboteur doesn't use AntagSelectionComponent, so we bypass
                // ForceMakeAntag and drive the saboteur pipeline directly.
                if (targetPlayer.AttachedEntity is not { } target)
                    return;

                var ruleEnt = ForceGetSaboteurRuleEnt();
                Entity<SaboteurRuleComponent?> rule = (ruleEnt.Owner, ruleEnt.Comp);
                _saboteurRule.MakeSaboteur((target, null), rule);
            },
            Impact = LogImpact.High,
            Message = string.Join(": ", saboteurName, Loc.GetString("admin-verb-make-saboteur")),
        };
        args.Verbs.Add(saboteurVerb);
    }

    /// <summary>
    /// Finds an existing Saboteur game rule entity or creates and starts a new one.
    /// Unlike <see cref="AntagSelectionSystem.ForceGetGameRuleEnt{T}"/>, this does
    /// not require <c>AntagSelectionComponent</c>.
    /// </summary>
    private Entity<SaboteurRuleComponent> ForceGetSaboteurRuleEnt()
    {
        var query = EntityQueryEnumerator<SaboteurRuleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            return (uid, comp);
        }

        var ruleEnt = _gameTicker.AddGameRule(DefaultSaboteurRule);
        RemComp<LoadMapRuleComponent>(ruleEnt);
        _gameTicker.StartGameRule(ruleEnt);
        return (ruleEnt, Comp<SaboteurRuleComponent>(ruleEnt));
    }
}
