// SPDX-FileCopyrightText: 2026 OmuStation Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Mind.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusEffectNew;
using Content.Omu.Shared.Enraged;

namespace Content.Omu.Server.Enraged;

public sealed partial class EnragedStatusEffectSystem : EntitySystem
{
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Apply hostile AI when the status effect starts, and revert when it ends.
        SubscribeLocalEvent<EnragedStatusEffectComponent, StatusEffectAppliedEvent>(OnStatusApplied);
        SubscribeLocalEvent<EnragedStatusEffectComponent, StatusEffectRemovedEvent>(OnStatusRemoved);
    }

    private void OnStatusApplied(Entity<EnragedStatusEffectComponent> ent, ref StatusEffectAppliedEvent args)
    {
        var target = args.Target;

        // Remove SSD indicator while raging.
        if (TryComp<SSDIndicatorComponent>(target, out _))
        {
            RemComp<SSDIndicatorComponent>(target);
            ent.Comp.RemovedSsdIndicator = true;
        }

        // Swap factions to hostile for the duration of the effect.
        if (!TryComp<NpcFactionMemberComponent>(target, out var npcFaction))
        {
            npcFaction = EnsureComp<NpcFactionMemberComponent>(target);
            ent.Comp.AddedFactionComponent = true;
        }

        ent.Comp.OldFactions.Clear();
        ent.Comp.OldFactions.UnionWith(npcFaction.Factions);
        _npcFaction.ClearFactions((target, npcFaction), false);
        _npcFaction.AddFaction((target, npcFaction), ent.Comp.HostileFaction);

        // Ensure HTN AI is present and switch to hostile behavior.
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

        // Wake and replan so the new hostile task takes effect immediately.
        _npc.WakeNPC(target, htn);
        _htn.Replan(htn);
    }

    private void OnStatusRemoved(Entity<EnragedStatusEffectComponent> ent, ref StatusEffectRemovedEvent args)
    {
        var target = args.Target;

        // Restore SSD indicator after the rage ends.
        if (ent.Comp.RemovedSsdIndicator && !HasComp<SSDIndicatorComponent>(target))
        {
            var ssd = EnsureComp<SSDIndicatorComponent>(target);
            ssd.IsSSD = false;
            Dirty(target, ssd);
        }

        // Restore original factions or remove the component if we added it.
        if (TryComp<NpcFactionMemberComponent>(target, out var npcFaction))
        {
            _npcFaction.ClearFactions((target, npcFaction), false);

            if (ent.Comp.AddedFactionComponent)
            {
                RemComp<NpcFactionMemberComponent>(target);
            }
            else
            {
                foreach (var faction in ent.Comp.OldFactions)
                {
                    _npcFaction.AddFaction((target, npcFaction), faction);
                }
            }
        }

        // Restore HTN task or remove the component if we added it.
        if (TryComp<HTNComponent>(target, out var htn))
        {
            if (ent.Comp.AddedHtnComponent)
            {
                RemComp<HTNComponent>(target);
            }
            else if (!string.IsNullOrEmpty(ent.Comp.OldRootTask))
            {
                htn.RootTask = new HTNCompoundTask { Task = ent.Comp.OldRootTask };
                _htn.Replan(htn);
            }
        }
    }
}
