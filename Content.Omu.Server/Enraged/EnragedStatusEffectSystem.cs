// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Omu.Shared.Enraged;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusEffectNew;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Omu.Server.Enraged;

/// <summary>
/// Swaps NPC factions and HTN tasks, allows HTN to drive players and restores previous state on expiry.
/// </summary>
public sealed partial class EnragedStatusEffectSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly EnragedBypassPrediction _bypass = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EnragedStatusEffectComponent, StatusEffectAppliedEvent>(OnStatusApplied);
        SubscribeLocalEvent<EnragedStatusEffectComponent, StatusEffectRemovedEvent>(OnStatusRemoved);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        _bypass.OnMeleeHit(weapon, ref args);
    }

    // ──────────────────────────────────────────────
    //  Status-effect lifecycle
    // ──────────────────────────────────────────────

    private void OnStatusApplied(Entity<EnragedStatusEffectComponent> ent, ref StatusEffectAppliedEvent args)
    {
        var target = args.Target;

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(target):target} became enraged.");

        RemoveSsdIndicator(ent, target);
        ApplyHostileFaction(ent, target);
        ApplyHostileHtn(ent, target);
    }

    private void OnStatusRemoved(Entity<EnragedStatusEffectComponent> ent, ref StatusEffectRemovedEvent args)
    {
        var target = args.Target;

        RestoreSsdIndicator(ent, target);
        RestoreFaction(ent, target);
        RestoreHtn(ent, target);
    }

    // ──────────────────────────────────────────────
    //  SSD indicator helpers
    // ──────────────────────────────────────────────

    private void RemoveSsdIndicator(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<SSDIndicatorComponent>(target, out _))
            return;

        RemComp<SSDIndicatorComponent>(target);
        ent.Comp.RemovedSsdIndicator = true;
    }

    private void RestoreSsdIndicator(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!ent.Comp.RemovedSsdIndicator || HasComp<SSDIndicatorComponent>(target))
            return;

        var ssd = EnsureComp<SSDIndicatorComponent>(target);
        ssd.IsSSD = false;
        Dirty(target, ssd);
    }

    // ──────────────────────────────────────────────
    //  NPC faction helpers
    // ──────────────────────────────────────────────

    private void ApplyHostileFaction(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<NpcFactionMemberComponent>(target, out var npcFaction))
        {
            npcFaction = EnsureComp<NpcFactionMemberComponent>(target);
            ent.Comp.AddedFactionComponent = true;
        }

        foreach (var f in npcFaction.Factions)
            ent.Comp.OldFactions.Add(f.ToString());

        _npcFaction.ClearFactions((target, npcFaction), false);
        _npcFaction.AddFaction((target, npcFaction), ent.Comp.HostileFaction);
    }

    private void RestoreFaction(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<NpcFactionMemberComponent>(target, out var npcFaction))
            return;

        _npcFaction.ClearFactions((target, npcFaction), false);

        if (ent.Comp.AddedFactionComponent)
        {
            RemComp<NpcFactionMemberComponent>(target);
            return;
        }

        foreach (var faction in ent.Comp.OldFactions)
        {
            _npcFaction.AddFaction((target, npcFaction), faction);
        }
    }

    // ──────────────────────────────────────────────
    //  HTN AI helpers
    // ──────────────────────────────────────────────

    private void ApplyHostileHtn(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<HTNComponent>(target, out var htn))
        {
            htn = EnsureComp<HTNComponent>(target);
            ent.Comp.AddedHtnComponent = true;
        }
        else
        {
            ent.Comp.OldRootTask = htn.RootTask.Task;
        }

        htn.RootTask = new HTNCompoundTask { Task = ent.Comp.HostileRootTask };
        htn.Blackboard.SetValue(NPCBlackboard.Owner, target);

        _npc.WakeNPC(target, htn);
        _htn.Replan(htn);
    }

    private void RestoreHtn(Entity<EnragedStatusEffectComponent> ent, EntityUid target)
    {
        if (!TryComp<HTNComponent>(target, out var htn))
            return;

        if (ent.Comp.AddedHtnComponent)
        {
            RemComp<HTNComponent>(target);
            return;
        }

        if (!string.IsNullOrEmpty(ent.Comp.OldRootTask))
        {
            htn.RootTask = new HTNCompoundTask { Task = ent.Comp.OldRootTask };
            _htn.Replan(htn);
        }
    }
}

