// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Content.Server._Omu.Saboteur;
using Content.Server._Omu.Saboteur.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Conditions;
using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Conditions.Systems;
using Content.Server.GameTicking;
using Content.Shared._Omu.Saboteur;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Contraband;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Omu.Saboteur;

[TestFixture]
public sealed class SaboteurRuleTest
{
    private static readonly EntProtoId SaboteurGameRuleProtoId = "Saboteur";

    [Test]
    public async Task TestSaboteurGameRulePrototype()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.That(protoMan.TryIndex<EntityPrototype>(SaboteurGameRuleProtoId, out var ruleProto),
                $"Failed to find Saboteur game rule entity prototype '{SaboteurGameRuleProtoId}'.");

            Assert.That(ruleProto!.TryGetComponent<SaboteurRuleComponent>(out var rule, compFact),
                "Saboteur game rule entity missing SaboteurRuleComponent.");

            Assert.That(rule!.Tiers, Is.Not.Empty, "Tiers list is empty.");

            Assert.That(ruleProto.TryGetComponent<SaboteurDepartmentConfigComponent>(out var deptConfig, compFact),
                "Saboteur game rule entity missing SaboteurDepartmentConfigComponent.");
            Assert.That(deptConfig!.CommandAccessTag, Is.Not.Empty, "CommandAccessTag must not be empty.");
            Assert.That(deptConfig.CommandDepartmentId, Is.Not.Empty, "CommandDepartmentId must not be empty.");
            Assert.That(deptConfig.DepartmentHeadMap, Is.Not.Empty, "DepartmentHeadMap must not be empty.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestAllSaboteurObjectivePrototypesHaveConditions()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        var conditionTypes = new[]
        {
            typeof(SaboteurMapThresholdConditionComponent),
            typeof(SaboteurFlaggedRecordsConditionComponent),
            typeof(SaboteurPlantEvidenceConditionComponent),
            typeof(SaboteurJobMismatchConditionComponent),
            typeof(SaboteurTakeBridgeControlConditionComponent),
            typeof(SaboteurHijackBudgetConditionComponent),
            typeof(SaboteurChainOfFoolsConditionComponent),
            typeof(SaboteurFakeAgentConditionComponent),
            typeof(SaboteurSubvertAIConditionComponent),
            typeof(SaboteurEmagBorgConditionComponent),
        };

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.TryGetComponent<SaboteurOperationComponent>(out _, compFact))
                    continue;

                Assert.That(proto.TryGetComponent<ObjectiveComponent>(out _, compFact),
                    $"Saboteur objective '{proto.ID}' has SaboteurOperation but no Objective component.");

                var hasCondition = conditionTypes.Any(t =>
                {
                    var compName = compFact.GetComponentName(t);
                    return proto.Components.ContainsKey(compName);
                });

                Assert.That(hasCondition,
                    $"Saboteur objective '{proto.ID}' has SaboteurOperation but no recognized condition component.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestFakeAgentConditionPrototypeValidity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurFakeAgentConditionComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurFakeAgentConditionComponent) reg.Component;

                switch (comp.Mode)
                {
                    case FakeAgentMode.PuppetInJob:

                        Assert.That(comp.TargetJobs, Is.Not.Empty,
                            $"FakeAgent objective '{proto.ID}' with PuppetInJob mode must specify targetJobs.");
                        break;
                    case FakeAgentMode.CoverAllJobs:

                        Assert.That(comp.TargetJobs.Count, Is.GreaterThan(1),
                            $"FakeAgent objective '{proto.ID}' with CoverAllJobs mode should have multiple targetJobs.");
                        break;
                    case FakeAgentMode.SelfSoleHolder:

                        break;
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestJobMismatchConditionPrototypeValidity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurJobMismatchConditionComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurJobMismatchConditionComponent) reg.Component;

                Assert.That(comp.RequiredCount, Is.GreaterThan(0),
                    $"JobMismatch objective '{proto.ID}' must have RequiredCount > 0.");

                if (comp.MismatchMode == JobMismatchMode.DemotedFromCommand)
                {
                    Assert.That(comp.FilterToCommandRecords, Is.True,
                        $"JobMismatch objective '{proto.ID}' with DemotedFromCommand mode should have FilterToCommandRecords=true.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestSaboteurRuleStarts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));

        SaboteurRuleComponent? sabRule = null;
        await server.WaitPost(() =>
        {
            var gameRuleEnt = ticker.AddGameRule(SaboteurGameRuleProtoId);
            Assert.That(entMan.TryGetComponent(gameRuleEnt, out sabRule),
                "Failed to get SaboteurRuleComponent after adding game rule.");
        });

        Assert.That(sabRule, Is.Not.Null);
        Assert.That(sabRule!.Tiers, Has.Count.GreaterThanOrEqualTo(2),
            "Must have at least 2 tiers defined.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestMapThresholdTargetComponentNamesAreValid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurMapThresholdConditionComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurMapThresholdConditionComponent) reg.Component;

                Assert.That(string.IsNullOrEmpty(comp.TargetComponent), Is.False,
                    $"MapThreshold objective '{proto.ID}' has empty targetComponent.");

                Assert.That(compFact.TryGetRegistration(comp.TargetComponent, out _),
                    $"MapThreshold objective '{proto.ID}' has unrecognized targetComponent '{comp.TargetComponent}'.");

                if (!string.IsNullOrEmpty(comp.SecondaryTargetComponent))
                {
                    Assert.That(compFact.TryGetRegistration(comp.SecondaryTargetComponent, out _),
                        $"MapThreshold objective '{proto.ID}' has unrecognized secondaryTargetComponent '{comp.SecondaryTargetComponent}'.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestHijackBudgetConditionPrototypeValidity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurHijackBudgetConditionComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurHijackBudgetConditionComponent) reg.Component;

                foreach (var (deptId, accountId) in comp.DepartmentOverrideMap)
                {
                    Assert.That(protoMan.HasIndex<DepartmentPrototype>(deptId),
                        $"HijackBudget objective '{proto.ID}' references unknown department '{deptId}'.");
                    Assert.That(protoMan.HasIndex<CargoAccountPrototype>(accountId),
                        $"HijackBudget objective '{proto.ID}' references unknown cargo account '{accountId}'.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestChainOfFoolsConditionPrototypeValidity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurChainOfFoolsConditionComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurChainOfFoolsConditionComponent) reg.Component;

                Assert.That(comp.RequiredCount, Is.GreaterThan(0),
                    $"ChainOfFools objective '{proto.ID}' must have RequiredCount > 0.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestBridgeControlConditionPrototypeValidity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurTakeBridgeControlConditionComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurTakeBridgeControlConditionComponent) reg.Component;

                Assert.That(comp.RequiredCount, Is.GreaterThan(0),
                    $"BridgeControl objective '{proto.ID}' must have RequiredCount > 0.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestPlantEvidenceConditionPrototypeValidity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurPlantEvidenceConditionComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurPlantEvidenceConditionComponent) reg.Component;

                Assert.That(protoMan.HasIndex<ContrabandSeverityPrototype>(comp.ContrabandSeverity),
                    $"PlantEvidence objective '{proto.ID}' references unknown contraband severity '{comp.ContrabandSeverity}'.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestFlaggedRecordsConditionPrototypeValidity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurFlaggedRecordsConditionComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurFlaggedRecordsConditionComponent) reg.Component;

                Assert.That(comp.RequiredCount, Is.GreaterThan(0),
                    $"FlaggedRecords objective '{proto.ID}' must have RequiredCount > 0.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestAllSaboteurOperationPrototypesReferencedByObjectives()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var referencedOps = new HashSet<string>();

            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurOperationComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurOperationComponent) reg.Component;
                referencedOps.Add(comp.OperationId);
            }

            foreach (var opProto in protoMan.EnumeratePrototypes<SaboteurOperationPrototype>())
            {
                Assert.That(referencedOps, Does.Contain(opProto.ID),
                    $"SaboteurOperationPrototype '{opProto.ID}' is not referenced by any objective entity.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestSaboteurOperationComponentReputationGainPositive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurOperationComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurOperationComponent) reg.Component;

                Assert.That(comp.ReputationGain, Is.GreaterThan(0),
                    $"Saboteur objective '{proto.ID}' has non-positive ReputationGain ({comp.ReputationGain}).");

                Assert.That(protoMan.HasIndex<SaboteurOperationPrototype>(comp.OperationId),
                    $"Saboteur objective '{proto.ID}' references unknown operation prototype '{comp.OperationId}'.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that every saboteur objective condition system's
    /// <see cref="RequirementCheckEvent"/> handler cancels assignment in an empty
    /// server (no stations, crew, borgs, or AI). A condition system missing its
    /// handler allows impossible objectives through uncancelled.
    /// </summary>
    /// <remarks>
    /// <see cref="FakeAgentMode.PuppetInJob"/> and <see cref="FakeAgentMode.CoverAllJobs"/>
    /// objectives are excluded because their handlers legitimately pass when
    /// <c>TargetJobs</c> is non-empty (configured from YAML). Those modes are
    /// tested by <see cref="TestFakeAgentRequirementCheckCancelsWithEmptyTargetJobs"/>.
    /// </remarks>
    [Test]
    public async Task TestAllSaboteurConditionsCancelInEmptyServer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
        });

        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        var fakeAgentPassModes = new HashSet<FakeAgentMode>
        {
            FakeAgentMode.PuppetInJob,
            FakeAgentMode.CoverAllJobs,
        };

        await server.WaitAssertion(() =>
        {
            var fakeAgentCompName = compFact.GetComponentName(typeof(SaboteurFakeAgentConditionComponent));

            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract)
                    continue;

                if (!proto.TryGetComponent<SaboteurOperationComponent>(out _, compFact))
                    continue;

                // FakeAgent PuppetInJob / CoverAllJobs legitimately pass — tested separately.
                if (proto.Components.TryGetValue(fakeAgentCompName, out var fakeReg))
                {
                    var fakeComp = (SaboteurFakeAgentConditionComponent) fakeReg.Component;
                    if (fakeAgentPassModes.Contains(fakeComp.Mode))
                        continue;
                }

                var objUid = entMan.SpawnEntity(proto.ID, MapCoordinates.Nullspace);

                var mindUid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
                var mind = entMan.AddComponent<MindComponent>(mindUid);

                var ev = new RequirementCheckEvent(mindUid, mind);
                entMan.EventBus.RaiseLocalEvent(objUid, ref ev);

                Assert.That(ev.Cancelled, Is.True,
                    $"Objective '{proto.ID}' was NOT cancelled in an empty server — " +
                    "its condition system is likely missing a RequirementCheckEvent handler.");

                entMan.DeleteEntity(objUid);
                entMan.DeleteEntity(mindUid);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that <see cref="FakeAgentMode.PuppetInJob"/> and
    /// <see cref="FakeAgentMode.CoverAllJobs"/> objectives cancel when their
    /// <c>TargetJobs</c> list is cleared. This ensures the handler exists and
    /// properly guards against empty job lists.
    /// </summary>
    [Test]
    public async Task TestFakeAgentRequirementCheckCancelsWithEmptyTargetJobs()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
        });

        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        var targetModes = new HashSet<FakeAgentMode>
        {
            FakeAgentMode.PuppetInJob,
            FakeAgentMode.CoverAllJobs,
        };

        await server.WaitAssertion(() =>
        {
            var compName = compFact.GetComponentName(typeof(SaboteurFakeAgentConditionComponent));

            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract)
                    continue;

                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var protoComp = (SaboteurFakeAgentConditionComponent) reg.Component;
                if (!targetModes.Contains(protoComp.Mode))
                    continue;

                var objUid = entMan.SpawnEntity(proto.ID, MapCoordinates.Nullspace);
                var liveComp = entMan.GetComponent<SaboteurFakeAgentConditionComponent>(objUid);
                // Direct mutation justified: no public API exists to clear TargetJobs.
                // This test verifies the handler rejects the empty-jobs edge case.
#pragma warning disable RA0002
                liveComp.TargetJobs.Clear();
#pragma warning restore RA0002

                var mindUid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
                var mind = entMan.AddComponent<MindComponent>(mindUid);

                var ev = new RequirementCheckEvent(mindUid, mind);
                entMan.EventBus.RaiseLocalEvent(objUid, ref ev);

                Assert.That(ev.Cancelled, Is.True,
                    $"FakeAgent objective '{proto.ID}' ({protoComp.Mode}) was NOT cancelled " +
                    "with empty TargetJobs — handler should reject this state.");

                entMan.DeleteEntity(objUid);
                entMan.DeleteEntity(mindUid);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests the <see cref="SaboteurOperationComponent"/> requirement handler in isolation:
    /// it must allow new operations and block operations that have already been completed.
    /// Uses a bare entity with no condition component so only the operation handler fires.
    /// </summary>
    [Test]
    public async Task TestOperationRequirementCheckBlocksCompletedOperations()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
        });

        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            // Bare entity with only the operation component — no condition handler fires.
            var objUid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<SaboteurOperationComponent>(objUid);
            var opComp = entMan.GetComponent<SaboteurOperationComponent>(objUid);

            var mindUid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var mind = entMan.AddComponent<MindComponent>(mindUid);
            var sabMind = entMan.AddComponent<SaboteurMindComponent>(mindUid);

            // New operation → not cancelled.
            var ev1 = new RequirementCheckEvent(mindUid, mind);
            entMan.EventBus.RaiseLocalEvent(objUid, ref ev1);
            Assert.That(ev1.Cancelled, Is.False,
                "Operation handler should pass for a new (non-completed) operation.");

            // Mark as completed → cancelled.
            // Direct mutation justified: CompleteOperation requires a full rule + mind
            // setup. This test intentionally uses a bare entity to isolate the
            // RequirementCheckEvent handler without triggering side effects.
#pragma warning disable RA0002
            sabMind.CompletedOperations.Add(opComp.OperationId);
#pragma warning restore RA0002
            var ev2 = new RequirementCheckEvent(mindUid, mind);
            entMan.EventBus.RaiseLocalEvent(objUid, ref ev2);
            Assert.That(ev2.Cancelled, Is.True,
                "Operation handler should cancel for an already-completed operation.");

            entMan.DeleteEntity(objUid);
            entMan.DeleteEntity(mindUid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Validates that <see cref="SaboteurMapThresholdConditionComponent"/> prototypes
    /// with <see cref="SaboteurMapThresholdConditionComponent.GroupByDepartment"/> set
    /// always specify a positive <see cref="SaboteurMapThresholdConditionComponent.MinGroupCount"/>.
    /// A zero or negative value would let the requirement check pass vacuously,
    /// allowing impossible lockdown objectives.
    /// </summary>
    [Test]
    public async Task TestMapThresholdGroupedModeHasMinGroupCount()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var compFact = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
            {
                var compName = compFact.GetComponentName(typeof(SaboteurMapThresholdConditionComponent));
                if (!proto.Components.TryGetValue(compName, out var reg))
                    continue;

                var comp = (SaboteurMapThresholdConditionComponent) reg.Component;

                if (comp.GroupByDepartment)
                {
                    Assert.That(comp.MinGroupCount, Is.GreaterThan(0),
                        $"MapThreshold objective '{proto.ID}' has GroupByDepartment=true " +
                        $"but MinGroupCount is {comp.MinGroupCount}, which would allow " +
                        "impossible lockdown objectives to be assigned.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
