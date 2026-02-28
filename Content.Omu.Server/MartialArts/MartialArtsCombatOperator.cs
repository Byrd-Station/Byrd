// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.CombatMode;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;

namespace Content.Omu.Server.MartialArts;

/// <summary>
/// HTN operator for NPC martial arts combo combat.
/// Adds NpcMartialArtsCombatComponent on startup so NpcMartialArtsCombatSystem can drive attacks.
/// </summary>
public sealed partial class MartialArtsCombatOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    [DataField("targetKey", required: true)]
    public string TargetKey = default!;

    [DataField("targetState")]
    public MobState TargetState = MobState.Alive;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var comp = _entManager.EnsureComponent<NpcMartialArtsCombatComponent>(owner);
        comp.Target = blackboard.GetValue<EntityUid>(TargetKey);
        _entManager.System<SharedCombatModeSystem>().SetInCombatMode(owner, true);
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entManager.System<SharedCombatModeSystem>().SetInCombatMode(owner, false);
        _entManager.RemoveComponent<NpcMartialArtsCombatComponent>(owner);
        blackboard.Remove<EntityUid>(TargetKey);
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return (false, null);

        if (_entManager.TryGetComponent<MobStateComponent>(target, out var mob) &&
            mob.CurrentState > TargetState)
            return (false, null);

        return (true, null);
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        ConditionalShutdown(blackboard);
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        base.PlanShutdown(blackboard);
        ConditionalShutdown(blackboard);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        base.Update(blackboard, frameTime);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<NpcMartialArtsCombatComponent>(owner, out var combat) ||
            !blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) ||
            target == EntityUid.Invalid)
        {
            return HTNOperatorStatus.Failed;
        }

        combat.Target = target;

        if (_entManager.TryGetComponent<MobStateComponent>(target, out var mob) &&
            mob.CurrentState > TargetState)
        {
            return HTNOperatorStatus.Finished;
        }

        if (combat.Status is not (CombatStatus.Normal or CombatStatus.TargetOutOfRange))
            return HTNOperatorStatus.Failed;

        return ShutdownState == HTNPlanState.PlanFinished
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Continuing;
    }
}
