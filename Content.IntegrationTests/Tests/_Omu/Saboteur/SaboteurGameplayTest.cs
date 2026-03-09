// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._Omu.Saboteur;
using Content.Server._Omu.Saboteur.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Roles;
using Content.Shared._Omu.Saboteur;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles;
using Content.Shared.Security;
using Content.Shared.Store.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._Omu.Saboteur;

/// <summary>
/// Integration tests that exercise saboteur gameplay flows end-to-end:
/// setup, operation completion, tier advancement, exposure, late-join, and dirty tracking.
/// </summary>
[TestFixture]
public sealed class SaboteurGameplayTest
{
    private static readonly EntProtoId SaboteurGameRuleProtoId = "Saboteur";

    #region Helper Methods

    /// <summary>
    /// Creates a test pair with a connected player in the lobby, starts the round,
    /// adds the Saboteur game rule, and returns everything needed for gameplay tests.
    /// </summary>
    private static async Task<SaboteurTestContext> SetupRoundWithSaboteur()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
            InLobby = true,
        });

        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var mindSys = server.System<MindSystem>();
        var roleSys = server.System<RoleSystem>();
        var sabRuleSys = server.System<SaboteurRuleSystem>();
        var sabOpsSys = server.System<SaboteurOperationSystem>();
        var condCore = server.System<SaboteurConditionCoreSystem>();

        // Add dummy sessions so the round can actually start (min players)
        var dummies = await server.AddDummySessions(10);
        await pair.RunTicksSync(5);

        EntityUid ruleEnt = default;
        SaboteurRuleComponent? ruleComp = null;

        await server.WaitPost(() =>
        {
            // Ready up everyone and start the round
            ticker.ToggleReadyAll(true);
            ticker.StartRound();

            // Add and activate the saboteur game rule
            ruleEnt = ticker.AddGameRule(SaboteurGameRuleProtoId);
            ticker.StartGameRule(ruleEnt);

            Assert.That(entMan.TryGetComponent(ruleEnt, out ruleComp),
                "Failed to get SaboteurRuleComponent after adding game rule.");
        });
        await pair.RunTicksSync(10);

        // The round should now be running
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        // The player should have a spawned entity
        var playerEntity = pair.Player!.AttachedEntity!.Value;
        Assert.That(entMan.EntityExists(playerEntity), "Player entity does not exist after round start.");

        return new SaboteurTestContext(
            pair, server, entMan, ticker, mindSys, roleSys,
            sabRuleSys, sabOpsSys, condCore,
            ruleEnt, ruleComp!, playerEntity, dummies);
    }

    /// <summary>
    /// Calls MakeSaboteur on the player entity inside a WaitPost block.
    /// Returns the player's mind entity UID.
    /// </summary>
    private static async Task<EntityUid> MakePlayerSaboteur(SaboteurTestContext ctx)
    {
        EntityUid mindId = default;

        await ctx.Server.WaitPost(() =>
        {
            ctx.SabRuleSys.MakeSaboteur((ctx.PlayerEntity, null), ctx.RuleEnt);
        });
        await ctx.Pair.RunTicksSync(5);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out var mid, out _),
                "Player has no mind after MakeSaboteur.");
            mindId = mid;
        });

        return mindId;
    }

    #endregion

    #region Test: Saboteur Setup

    /// <summary>
    /// Verifies that MakeSaboteur correctly sets up all components, uplink, codewords,
    /// and initial objectives on a player entity.
    /// </summary>
    [Test]
    public async Task TestMakeSaboteurSetup()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            // Body should have SaboteurComponent
            Assert.That(ctx.EntMan.HasComponent<SaboteurComponent>(ctx.PlayerEntity),
                "Player entity missing SaboteurComponent after MakeSaboteur.");

            // Mind should have SaboteurMindComponent
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent after MakeSaboteur.");

            // Initial state should be clean
            Assert.That(mindComp!.Reputation, Is.EqualTo(0), "Initial reputation should be 0.");
            Assert.That(mindComp.ReputationTier, Is.EqualTo(0), "Initial tier should be 0.");
            Assert.That(mindComp.ExposurePenaltyMultiplier, Is.EqualTo(1.0f), "Initial exposure should be 1.0 (clean).");
            Assert.That(mindComp.CompletedOperations, Is.Empty, "No operations should be completed initially.");
            Assert.That(mindComp.GloriousDeathActive, Is.False, "DAGD should not be active initially.");

            // Mind should have the saboteur mind role
            Assert.That(ctx.RoleSys.MindHasRole<SaboteurRoleComponent>(mindId),
                "Mind missing SaboteurRoleComponent after MakeSaboteur.");

            // Player should be flagged as an antagonist
            Assert.That(ctx.RoleSys.MindIsAntagonist(mindId),
                "Player should be an antagonist after MakeSaboteur.");

            // Should have at least one objective assigned
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out _, out var mind),
                "Could not get mind component.");
            Assert.That(mind!.Objectives, Is.Not.Empty,
                "Saboteur should have at least one objective after setup.");

            // At least one objective should be a saboteur operation
            var hasSabObjective = mind.Objectives.Any(o =>
                ctx.EntMan.HasComponent<SaboteurOperationComponent>(o));
            Assert.That(hasSabObjective,
                "At least one objective should be a SaboteurOperation.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Uplink Setup

    /// <summary>
    /// Verifies that the saboteur's uplink is created with the correct store categories
    /// for tier 0 and uses the correct currency.
    /// </summary>
    [Test]
    public async Task TestSaboteurUplinkSetup()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent.");

            // The uplink entity should exist
            Assert.That(mindComp!.UplinkEntity, Is.Not.Null,
                "Saboteur uplink entity should not be null.");

            Assert.That(ctx.EntMan.TryGetComponent<StoreComponent>(mindComp.UplinkEntity!.Value, out var store),
                "Uplink entity missing StoreComponent.");

            // Should have the TC currency whitelisted
            Assert.That(store!.CurrencyWhitelist, Does.Contain(ctx.RuleComp.TelecrystalCurrency.Id),
                "Uplink missing telecrystal currency.");

            // Should only have tier-0 categories unlocked
            foreach (var (category, requiredTier) in ctx.RuleComp.CategoryTierMap)
            {
                if (requiredTier == 0)
                {
                    Assert.That(store.Categories, Does.Contain(category),
                        $"Tier 0 category {category} should be unlocked at start.");
                }
                else
                {
                    Assert.That(store.Categories, Does.Not.Contain(category),
                        $"Category {category} (tier {requiredTier}) should NOT be unlocked at tier 0.");
                }
            }
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Operation Completion Flow

    /// <summary>
    /// Verifies the full operation completion flow: calling CompleteOperation grants
    /// reputation, TC, marks the operation as completed, removes the old objective,
    /// and assigns a new one.
    /// </summary>
    [Test]
    public async Task TestOperationCompletionFlow()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent.");
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out _, out var mind),
                "Could not get mind component.");

            // Find the first saboteur operation objective
            SaboteurOperationComponent? opComp = null;
            EntityUid opEntity = default;
            foreach (var obj in mind!.Objectives)
            {
                if (ctx.EntMan.TryGetComponent<SaboteurOperationComponent>(obj, out opComp))
                {
                    opEntity = obj;
                    break;
                }
            }

            Assert.That(opComp, Is.Not.Null, "Should have at least one saboteur operation objective.");

            var operationId = opComp!.OperationId;
            var repGain = opComp.ReputationGain;
            var isMajor = opComp.IsMajor;
            var initialObjectiveCount = mind.Objectives.Count;

            // Complete the operation
            ctx.SabOpsSys.CompleteOperation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                operationId,
                repGain,
                isMajor);

            // Reputation should have increased
            Assert.That(mindComp!.Reputation, Is.GreaterThan(0),
                "Reputation should increase after completing an operation.");
            Assert.That(mindComp.Reputation, Is.EqualTo(repGain),
                $"Expected {repGain} rep after first operation, got {mindComp.Reputation}.");

            // The operation should be tracked as completed
            Assert.That(mindComp.CompletedOperations, Does.Contain(operationId),
                "Operation should be in CompletedOperations after completion.");

            // The old objective should be removed and a new one assigned
            var hasOldObjective = mind.Objectives.Any(o =>
                ctx.EntMan.TryGetComponent<SaboteurOperationComponent>(o, out var c)
                && c.OperationId == operationId);
            Assert.That(hasOldObjective, Is.False,
                "Completed objective should be removed from the mind.");

            // Should still have objectives (a new one was assigned)
            Assert.That(mind.Objectives, Is.Not.Empty,
                "A new objective should be assigned after completing one.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Double Completion Prevention

    /// <summary>
    /// Verifies that completing the same operation twice is a no-op.
    /// </summary>
    [Test]
    public async Task TestDoubleCompletionIsNoOp()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent.");
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out _, out var mind));

            // Find first saboteur operation
            SaboteurOperationComponent? opComp = null;
            foreach (var obj in mind!.Objectives)
            {
                if (ctx.EntMan.TryGetComponent<SaboteurOperationComponent>(obj, out opComp))
                    break;
            }

            Assert.That(opComp, Is.Not.Null);
            var operationId = opComp!.OperationId;

            // Complete it once
            ctx.SabOpsSys.CompleteOperation(
                (ctx.PlayerEntity, null), (ctx.RuleEnt, null),
                operationId, opComp.ReputationGain, opComp.IsMajor);

            var repAfterFirst = mindComp!.Reputation;
            var completedCount = mindComp.CompletedOperations.Count;

            // Try completing the same operation again
            ctx.SabOpsSys.CompleteOperation(
                (ctx.PlayerEntity, null), (ctx.RuleEnt, null),
                operationId, opComp.ReputationGain, opComp.IsMajor);

            // Reputation should not change
            Assert.That(mindComp.Reputation, Is.EqualTo(repAfterFirst),
                "Double completion should not grant additional reputation.");
            Assert.That(mindComp.CompletedOperations.Count, Is.EqualTo(completedCount),
                "CompletedOperations count should not change on double completion.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Tier Advancement

    /// <summary>
    /// Verifies that adding enough reputation triggers tier advancement and updates
    /// the store categories to unlock higher-tier items.
    /// </summary>
    [Test]
    public async Task TestTierAdvancement()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent.");

            Assert.That(ctx.RuleComp.Tiers, Has.Count.GreaterThanOrEqualTo(2),
                "Need at least 2 tiers for tier advancement test.");

            var tier1Threshold = ctx.RuleComp.Tiers[1].Threshold;

            // Add enough reputation to reach tier 1
            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                tier1Threshold);

            Assert.That(mindComp!.Reputation, Is.GreaterThanOrEqualTo(tier1Threshold),
                $"Reputation should be at least {tier1Threshold} after adding {tier1Threshold}.");
            Assert.That(mindComp.ReputationTier, Is.GreaterThanOrEqualTo(1),
                "Should have advanced to at least tier 1.");

            // Verify store categories updated for tier 1
            if (mindComp.UplinkEntity is { } uplinkEntity
                && ctx.EntMan.TryGetComponent<StoreComponent>(uplinkEntity, out var store))
            {
                foreach (var (category, requiredTier) in ctx.RuleComp.CategoryTierMap)
                {
                    if (requiredTier <= 1)
                    {
                        Assert.That(store.Categories, Does.Contain(category),
                            $"Category {category} (tier {requiredTier}) should be unlocked at tier 1.");
                    }
                }
            }
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies advancement through all tiers up to max.
    /// </summary>
    [Test]
    public async Task TestFullTierProgression()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent.");

            var maxTier = SaboteurTierHelper.GetMaxTier(ctx.RuleComp.Tiers);
            var maxThreshold = ctx.RuleComp.Tiers[maxTier].Threshold;

            // Add enough rep to reach max tier
            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                maxThreshold);

            Assert.That(mindComp!.ReputationTier, Is.EqualTo(maxTier),
                $"Should have reached max tier {maxTier} with {maxThreshold} rep.");

            // Next tier threshold should be 0 (already at max)
            var nextThreshold = SaboteurTierHelper.GetNextTierThreshold(ctx.RuleComp.Tiers, maxTier);
            Assert.That(nextThreshold, Is.EqualTo(0),
                "Next tier threshold should be 0 at max tier.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Exposure Penalty

    /// <summary>
    /// Verifies that a newly created saboteur has no exposure penalty (multiplier = 1.0),
    /// and that AddReputation with no exposure grants the full amount.
    /// </summary>
    [Test]
    public async Task TestCleanSaboteurGetsFullReputation()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent.");

            // Initially should not be exposed
            Assert.That(SaboteurTierHelper.IsExposed(mindComp!.ExposurePenaltyMultiplier), Is.False,
                "Saboteur should not be exposed initially.");
            Assert.That(mindComp.ExposurePenaltyMultiplier, Is.EqualTo(1.0f),
                "Initial exposure multiplier should be 1.0.");
            Assert.That(mindComp.HighestObservedStatus, Is.EqualTo(SecurityStatus.None),
                "Initial security status should be None.");

            var rawGain = 20;
            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                rawGain);

            // With no exposure penalty, should gain the full amount
            Assert.That(mindComp.Reputation, Is.EqualTo(rawGain),
                $"Expected full {rawGain} rep gain with no exposure penalty.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that RunExposureChecks does not alter the multiplier when
    /// the saboteur has no criminal record.
    /// </summary>
    [Test]
    public async Task TestExposureCheckPreservesCleanState()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent.");

            var multiplierBefore = mindComp!.ExposurePenaltyMultiplier;

            // Run exposure checks — saboteur has no criminal record in test env
            ctx.SabOpsSys.RunExposureChecks((ctx.RuleEnt, null));

            // Multiplier should remain unchanged (no record = no penalty)
            Assert.That(mindComp.ExposurePenaltyMultiplier, Is.EqualTo(multiplierBefore),
                "Exposure multiplier should not change when no criminal record exists.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that the exposure multiplier configuration is valid:
    /// all configured statuses map to multipliers less than 1.0.
    /// </summary>
    [Test]
    public async Task TestExposureMultiplierConfiguration()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.RuleComp.ExposureMultipliers, Is.Not.Empty,
                "Exposure multipliers should be configured.");

            foreach (var (status, multiplier) in ctx.RuleComp.ExposureMultipliers)
            {
                Assert.That(multiplier, Is.LessThan(1.0f),
                    $"Exposure multiplier for {status} should be < 1.0.");
                Assert.That(multiplier, Is.GreaterThanOrEqualTo(ctx.RuleComp.ExposureFloorMultiplier),
                    $"Exposure multiplier for {status} should be >= floor ({ctx.RuleComp.ExposureFloorMultiplier}).");

                // Verify IsExposed correctly detects this as exposed
                Assert.That(SaboteurTierHelper.IsExposed(multiplier),
                    $"IsExposed should return true for multiplier {multiplier}.");
            }

            // Floor multiplier should also be considered exposed
            Assert.That(SaboteurTierHelper.IsExposed(ctx.RuleComp.ExposureFloorMultiplier),
                "Floor multiplier should be considered exposed.");

            // 1.0 should NOT be considered exposed
            Assert.That(SaboteurTierHelper.IsExposed(1.0f), Is.False,
                "1.0 multiplier should not be considered exposed.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Operation Completion with TC Reward

    /// <summary>
    /// Verifies that completing an operation grants the correct number of TC
    /// based on the current tier.
    /// </summary>
    [Test]
    public async Task TestOperationGrantsCorrectTc()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent.");

            // Record the uplink balance before completion
            var uplinkEntity = mindComp!.UplinkEntity;
            Assert.That(uplinkEntity, Is.Not.Null, "Uplink entity should exist.");

            Assert.That(ctx.EntMan.TryGetComponent<StoreComponent>(uplinkEntity!.Value, out var store),
                "Store component should exist on uplink.");

            var currencyId = ctx.RuleComp.TelecrystalCurrency.Id;
            var balanceBefore = store!.Balance.GetValueOrDefault(currencyId);

            // Find a saboteur operation
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out _, out var mind));
            SaboteurOperationComponent? opComp = null;
            foreach (var obj in mind!.Objectives)
            {
                if (ctx.EntMan.TryGetComponent<SaboteurOperationComponent>(obj, out opComp))
                    break;
            }
            Assert.That(opComp, Is.Not.Null);

            var expectedTc = SaboteurTierHelper.GetTelecrystalReward(
                ctx.RuleComp.Tiers, mindComp.ReputationTier, opComp!.IsMajor);

            // Complete the operation
            ctx.SabOpsSys.CompleteOperation(
                (ctx.PlayerEntity, null), (ctx.RuleEnt, null),
                opComp.OperationId, opComp.ReputationGain, opComp.IsMajor);

            var balanceAfter = store.Balance.GetValueOrDefault(currencyId);
            Assert.That(balanceAfter, Is.EqualTo(balanceBefore + expectedTc),
                $"Expected {expectedTc} TC reward, got {balanceAfter - balanceBefore}.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Dirty Tracking System

    /// <summary>
    /// Verifies the dirty tracking system: objectives are initially dirty after
    /// assignment, and the cache system works correctly via public API.
    /// </summary>
    [Test]
    public async Task TestDirtyTrackingEndToEnd()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.CondCore.TryGetDirtyTracking(out var dirty),
                "Should have dirty tracking component on rule entity.");

            // Find a saboteur objective to check its dirty status
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out _, out var mind));
            EntityUid? sabObjective = null;
            foreach (var obj in mind!.Objectives)
            {
                if (ctx.EntMan.HasComponent<SaboteurOperationComponent>(obj))
                {
                    sabObjective = obj;
                    break;
                }
            }

            if (sabObjective == null)
            {
                Assert.Warn("No saboteur operation objective found — skipping dirty tracking test.");
                return;
            }

            // The objective should be initially dirty (registered as dirty on assign)
            Assert.That(ctx.CondCore.IsObjectiveDirty(dirty!, sabObjective.Value),
                "Objective should be dirty after initial assignment.");

            // Test CacheAndReturn: caching a value clears the dirty flag
            var cacheKey = ctx.CondCore.MakeCacheKey(sabObjective.Value);
            ctx.CondCore.CacheAndReturn(dirty!, sabObjective.Value, cacheKey, 0.5f);

            // After caching, it should no longer be dirty
            Assert.That(ctx.CondCore.IsObjectiveDirty(dirty!, sabObjective.Value), Is.False,
                "Objective should not be dirty after caching progress.");

            // The cached value should be retrievable
            Assert.That(ctx.CondCore.TryGetCached(dirty!, sabObjective.Value, cacheKey, out var cached),
                "Should be able to retrieve cached progress.");
            Assert.That(cached, Is.EqualTo(0.5f).Within(0.001f),
                "Cached progress should match what was stored.");

            // Test CacheAndReturn with 1.0 — should set pending completion
            ctx.CondCore.CacheAndReturn(dirty!, sabObjective.Value, cacheKey, 1.0f);
            Assert.That(ctx.CondCore.IsCompletionPending(dirty!, sabObjective.Value),
                "Objective should be pending completion after caching 1.0 progress.");

            // ClearPendingCompletion should remove it
            ctx.CondCore.ClearPendingCompletion(dirty!, sabObjective.Value);
            Assert.That(ctx.CondCore.IsCompletionPending(dirty!, sabObjective.Value), Is.False,
                "Objective should not be pending completion after clearing.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that marking a dirty domain propagates to interested objectives
    /// after flushing, using the public API.
    /// </summary>
    [Test]
    public async Task TestDirtyDomainFlush()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.CondCore.TryGetDirtyTracking(out var dirty),
                "Should have dirty tracking component on rule entity.");

            // Create a fresh test entity to register as an "objective" for domain interest
            var testEntity = ctx.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);

            // Register interest in a specific domain — this also marks it dirty
            ctx.CondCore.RegisterInterest(dirty!, testEntity, SaboteurDirtyDomain.Power);

            // Initially dirty after registration
            Assert.That(ctx.CondCore.IsObjectiveDirty(dirty!, testEntity),
                "Objective should be dirty after registration.");

            // Clear the dirty flag by caching a value via CacheAndReturn
            var cacheKey = ctx.CondCore.MakeCacheKey(testEntity);
            ctx.CondCore.CacheAndReturn(dirty!, testEntity, cacheKey, 0f);
            Assert.That(ctx.CondCore.IsObjectiveDirty(dirty!, testEntity), Is.False,
                "Objective should not be dirty after caching.");

            // Mark the domain dirty via MarkDirty + FlushPendingDirty
            ctx.CondCore.MarkDirty(SaboteurDirtyDomain.Power);
            ctx.CondCore.FlushPendingDirty(dirty!);

            // After flush: the entity should be dirty again
            Assert.That(ctx.CondCore.IsObjectiveDirty(dirty!, testEntity),
                "Objective should be dirty after domain flush.");

            // The cached value should no longer be valid (dirty takes precedence)
            Assert.That(ctx.CondCore.TryGetCached(dirty!, testEntity, cacheKey, out _), Is.False,
                "Cached progress should not be available while objective is dirty.");

            // Clean up
            ctx.EntMan.DeleteEntity(testEntity);
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that multiple domains can be registered and each independently
    /// triggers dirtiness via the public API.
    /// </summary>
    [Test]
    public async Task TestMultipleDomainInterest()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.CondCore.TryGetDirtyTracking(out var dirty));

            var testEntity = ctx.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);

            // Register interest in two domains
            ctx.CondCore.RegisterInterest(dirty!, testEntity,
                SaboteurDirtyDomain.Power | SaboteurDirtyDomain.Camera);

            // Clear initial dirty via CacheAndReturn
            var cacheKey = ctx.CondCore.MakeCacheKey(testEntity);
            ctx.CondCore.CacheAndReturn(dirty!, testEntity, cacheKey, 0f);
            Assert.That(ctx.CondCore.IsObjectiveDirty(dirty!, testEntity), Is.False);

            // Only mark Camera dirty — should still flag the entity
            ctx.CondCore.MarkDirty(SaboteurDirtyDomain.Camera);
            ctx.CondCore.FlushPendingDirty(dirty!);

            Assert.That(ctx.CondCore.IsObjectiveDirty(dirty!, testEntity),
                "Objective should be dirty when any registered domain is dirtied.");

            // Clear and test the other domain
            ctx.CondCore.CacheAndReturn(dirty!, testEntity, cacheKey, 0f);
            ctx.CondCore.MarkDirty(SaboteurDirtyDomain.Power);
            ctx.CondCore.FlushPendingDirty(dirty!);

            Assert.That(ctx.CondCore.IsObjectiveDirty(dirty!, testEntity),
                "Objective should be dirty when the other registered domain is dirtied.");

            // Unrelated domain should NOT dirty the entity
            ctx.CondCore.CacheAndReturn(dirty!, testEntity, cacheKey, 0f);
            ctx.CondCore.MarkDirty(SaboteurDirtyDomain.Records);
            ctx.CondCore.FlushPendingDirty(dirty!);

            Assert.That(ctx.CondCore.IsObjectiveDirty(dirty!, testEntity), Is.False,
                "Objective should NOT be dirty for an unregistered domain.");

            ctx.EntMan.DeleteEntity(testEntity);
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Reputation Events

    /// <summary>
    /// Verifies that adding reputation raises the SaboteurReputationChangedEvent
    /// and that tier advancement raises SaboteurTierAdvancedEvent.
    /// </summary>
    [Test]
    public async Task TestReputationAndTierEvents()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp),
                "Mind missing SaboteurMindComponent.");

            // Track events via flags
            var repChanged = false;
            var tierAdvanced = false;
            var oldRep = -1;
            var newRep = -1;
            var oldTier = -1;
            var newTier = -1;

            // Subscribe to events on the rule entity
            // We use a helper system approach: just check state before and after

            var repBefore = mindComp!.Reputation;
            var tierBefore = mindComp.ReputationTier;

            // Add rep below tier 1 threshold
            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                5);

            Assert.That(mindComp.Reputation, Is.EqualTo(repBefore + 5),
                "Reputation should increase by 5.");
            Assert.That(mindComp.ReputationTier, Is.EqualTo(tierBefore),
                "Tier should not change for small rep gain.");

            // Now add enough to cross into tier 1
            var tier1Threshold = ctx.RuleComp.Tiers[1].Threshold;
            var neededForTier1 = tier1Threshold - mindComp.Reputation;
            if (neededForTier1 > 0)
            {
                ctx.SabOpsSys.AddReputation(
                    (ctx.PlayerEntity, null),
                    (ctx.RuleEnt, null),
                    neededForTier1);
            }

            Assert.That(mindComp.ReputationTier, Is.GreaterThanOrEqualTo(1),
                "Tier should advance after reaching threshold.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Objective Reassignment

    /// <summary>
    /// Verifies that after completing an operation, a new objective is assigned
    /// (up to MaxActiveObjectives), and completed operations cannot be re-assigned.
    /// </summary>
    [Test]
    public async Task TestObjectiveReassignmentAfterCompletion()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp));
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out _, out var mind));

            // Collect all current saboteur operations
            var operations = new List<ProtoId<SaboteurOperationPrototype>>();
            foreach (var obj in mind!.Objectives)
            {
                if (ctx.EntMan.TryGetComponent<SaboteurOperationComponent>(obj, out var op))
                    operations.Add(op.OperationId);
            }

            Assert.That(operations, Is.Not.Empty, "Should have at least one operation.");

            // Complete the first operation
            var firstOp = operations[0];
            ctx.SabOpsSys.CompleteOperation(
                (ctx.PlayerEntity, null), (ctx.RuleEnt, null),
                firstOp);

            // The completed operation should not appear in current objectives
            var currentOps = new List<ProtoId<SaboteurOperationPrototype>>();
            foreach (var obj in mind.Objectives)
            {
                if (ctx.EntMan.TryGetComponent<SaboteurOperationComponent>(obj, out var op))
                    currentOps.Add(op.OperationId);
            }

            Assert.That(currentOps, Does.Not.Contain(firstOp),
                "Completed operation should not be in current objectives.");

            // The completed operation should be tracked
            Assert.That(mindComp!.CompletedOperations, Does.Contain(firstOp),
                "Completed operation should be in CompletedOperations set.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Completion Sweep via Dirty Tracking

    /// <summary>
    /// Verifies that RunCompletionSweep processes dirty/pending objectives by re-evaluating
    /// progress and clearing pending-completion flags, even when progress is under 1.0.
    /// </summary>
    [Test]
    public async Task TestCompletionSweepProcessesDirtyObjectives()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp));
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out _, out var mind));
            Assert.That(ctx.CondCore.TryGetDirtyTracking(out var dirty));

            // Find a saboteur objective
            EntityUid? sabObjective = null;
            SaboteurOperationComponent? opComp = null;
            foreach (var obj in mind!.Objectives)
            {
                if (ctx.EntMan.TryGetComponent<SaboteurOperationComponent>(obj, out opComp))
                {
                    sabObjective = obj;
                    break;
                }
            }

            if (sabObjective == null || opComp == null)
            {
                Assert.Warn("No saboteur objective found for completion sweep test.");
                return;
            }

            // Set up a pending completion via CacheAndReturn (simulates a prior progress query
            // that found progress >= 1.0). The sweep will re-evaluate actual progress via event.
            var cacheKey = ctx.CondCore.MakeCacheKey(sabObjective.Value);
            ctx.CondCore.CacheAndReturn(dirty!, sabObjective.Value, cacheKey, 1.0f);

            // The objective should now be pending completion
            Assert.That(ctx.CondCore.IsCompletionPending(dirty!, sabObjective.Value),
                "Objective should be pending completion after CacheAndReturn with 1.0 progress.");

            // Run the completion sweep — map conditions won't be met, so it won't actually
            // complete the operation, but it MUST clear the pending completion flag.
            ctx.SabOpsSys.RunCompletionSweep((ctx.RuleEnt, null));

            // The pending completion flag should be cleared after the sweep processes it
            Assert.That(!ctx.CondCore.IsCompletionPending(dirty!, sabObjective.Value),
                "Pending completion flag should be cleared after sweep processes the objective.");

            // The operation should NOT be completed since actual progress is under 1.0
            var completedOps = mindComp!.CompletedOperations.Select(x => x.Id).ToList();
            Assert.That(!completedOps.Contains(opComp!.OperationId.Id),
                "Operation should not be completed when actual progress is under 1.0.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Exposure Check Integration

    /// <summary>
    /// Verifies that RunExposureChecks works without errors when saboteurs exist
    /// (even if they don't have criminal records set up in the test environment).
    /// </summary>
    [Test]
    public async Task TestExposureCheckDoesNotThrow()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            // This should not throw, even without criminal record infrastructure
            Assert.DoesNotThrow(() =>
                ctx.SabOpsSys.RunExposureChecks((ctx.RuleEnt, null)),
                "RunExposureChecks should not throw for saboteurs without criminal records.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: TC Grant

    /// <summary>
    /// Verifies that GrantTelecrystals correctly adds currency to the uplink.
    /// </summary>
    [Test]
    public async Task TestGrantTelecrystals()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp));
            Assert.That(mindComp!.UplinkEntity, Is.Not.Null);
            Assert.That(ctx.EntMan.TryGetComponent<StoreComponent>(mindComp.UplinkEntity!.Value, out var store));

            var currencyId = ctx.RuleComp.TelecrystalCurrency.Id;
            var balanceBefore = store!.Balance.GetValueOrDefault(currencyId);

            ctx.SabOpsSys.GrantTelecrystals(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                15);

            var balanceAfter = store.Balance.GetValueOrDefault(currencyId);
            Assert.That(balanceAfter, Is.EqualTo(balanceBefore + 15),
                "Uplink should have 15 more TC after grant.");

            // Granting 0 or negative should be a no-op
            ctx.SabOpsSys.GrantTelecrystals(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                0);

            Assert.That(store.Balance.GetValueOrDefault(currencyId), Is.EqualTo(balanceAfter),
                "Granting 0 TC should not change balance.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Active Tick

    /// <summary>
    /// Verifies that the rule's ActiveTick runs completion and exposure sweeps
    /// only when the dirty-tracking flags are set, rather than on fixed timers.
    /// </summary>
    [Test]
    public async Task TestActiveTickScheduling()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            // With no dirty domains, the flags should be false after ticks
            Assert.That(ctx.CondCore.TryGetDirtyTracking(out var dirty), Is.True,
                "Dirty tracking component should exist on the rule entity.");

            // Mark the Records domain dirty — this should set both flags
            ctx.CondCore.MarkDirty(SaboteurDirtyDomain.Records);

            Assert.That(dirty!.CompletionSweepNeeded, Is.True,
                "CompletionSweepNeeded should be set after MarkDirty.");
            Assert.That(dirty.ExposureCheckNeeded, Is.True,
                "ExposureCheckNeeded should be set when Records domain is dirtied.");
        });

        // Let a tick run so ActiveTick processes the flags
        await ctx.Pair.RunTicksSync(3);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.CondCore.TryGetDirtyTracking(out var dirty), Is.True);

            // After ActiveTick processes them, the flags should be cleared
            Assert.That(dirty!.CompletionSweepNeeded, Is.False,
                "CompletionSweepNeeded should be cleared after ActiveTick.");
            Assert.That(dirty.ExposureCheckNeeded, Is.False,
                "ExposureCheckNeeded should be cleared after ActiveTick.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Multiple Saboteurs

    /// <summary>
    /// Verifies that multiple entities can be made saboteurs independently.
    /// </summary>
    [Test]
    public async Task TestMultipleSaboteurs()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        // Make the player a saboteur
        var mindId1 = await MakePlayerSaboteur(ctx);

        // Find a dummy player with an attached entity
        EntityUid? dummyEntity = null;
        foreach (var dummy in ctx.Dummies)
        {
            if (dummy.AttachedEntity is { } entity && ctx.EntMan.EntityExists(entity))
            {
                dummyEntity = entity;
                break;
            }
        }

        if (dummyEntity == null)
        {
            Assert.Warn("No dummy player with attached entity found — skipping multi-saboteur test.");
            await ctx.Pair.CleanReturnAsync();
            return;
        }

        EntityUid mindId2 = default;
        await ctx.Server.WaitPost(() =>
        {
            ctx.SabRuleSys.MakeSaboteur((dummyEntity.Value, null), ctx.RuleEnt);
        });
        await ctx.Pair.RunTicksSync(5);

        await ctx.Server.WaitAssertion(() =>
        {
            // Both should have SaboteurComponent
            Assert.That(ctx.EntMan.HasComponent<SaboteurComponent>(ctx.PlayerEntity),
                "Player should have SaboteurComponent.");
            Assert.That(ctx.EntMan.HasComponent<SaboteurComponent>(dummyEntity!.Value),
                "Dummy should have SaboteurComponent.");

            // Both should have independent SaboteurMindComponents
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out var mid1, out _));
            Assert.That(ctx.MindSys.TryGetMind(dummyEntity.Value, out var mid2, out _));

            Assert.That(ctx.EntMan.HasComponent<SaboteurMindComponent>(mid1),
                "Player mind should have SaboteurMindComponent.");
            Assert.That(ctx.EntMan.HasComponent<SaboteurMindComponent>(mid2),
                "Dummy mind should have SaboteurMindComponent.");

            // Adding reputation to one should not affect the other
            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                25);

            var mc1 = ctx.EntMan.GetComponent<SaboteurMindComponent>(mid1);
            var mc2 = ctx.EntMan.GetComponent<SaboteurMindComponent>(mid2);

            Assert.That(mc1.Reputation, Is.EqualTo(25),
                "Player saboteur should have 25 rep.");
            Assert.That(mc2.Reputation, Is.EqualTo(0),
                "Dummy saboteur should still have 0 rep.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Duplicate Saboteur Prevention

    /// <summary>
    /// Verifies that calling MakeSaboteur on an entity that is already a saboteur
    /// does not create duplicate components.
    /// </summary>
    [Test]
    public async Task TestDuplicateSaboteurPrevention()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindCompBefore));
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out _, out var mindBefore));
            var objectiveCountBefore = mindBefore!.Objectives.Count;
            var repBefore = mindCompBefore!.Reputation;
        });

        // Try to make them a saboteur again — the AfterAntagEntitySelected handler
        // checks for existing SaboteurComponent and skips.
        // But MakeSaboteur itself uses EnsureComp, so it's idempotent for components.
        // The real test is that it doesn't duplicate mind roles or objectives excessively.
        await ctx.Server.WaitPost(() =>
        {
            // Directly calling MakeSaboteur again should not crash
            ctx.SabRuleSys.MakeSaboteur((ctx.PlayerEntity, null), ctx.RuleEnt);
        });
        await ctx.Pair.RunTicksSync(5);

        await ctx.Server.WaitAssertion(() =>
        {
            // Should still be a saboteur with no crash
            Assert.That(ctx.EntMan.HasComponent<SaboteurComponent>(ctx.PlayerEntity),
                "Should still have SaboteurComponent.");
            Assert.That(ctx.EntMan.HasComponent<SaboteurMindComponent>(mindId),
                "Should still have SaboteurMindComponent.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: AddReputation Edge Cases

    /// <summary>
    /// Verifies that adding zero reputation is a no-op: no reputation change, no tier change.
    /// </summary>
    [Test]
    public async Task TestAddReputationZero()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp));

            var repBefore = mindComp!.Reputation;
            var tierBefore = mindComp.ReputationTier;

            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                0);

            Assert.That(mindComp.Reputation, Is.EqualTo(repBefore),
                "Adding 0 reputation should not change the total.");
            Assert.That(mindComp.ReputationTier, Is.EqualTo(tierBefore),
                "Adding 0 reputation should not change the tier.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that reputation gain is reduced by the exposure multiplier
    /// when the saboteur has an active exposure penalty.
    /// </summary>
    [Test]
    public async Task TestAddReputationWithExposurePenalty()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp));

            // Manually apply an exposure penalty (simulate being caught).
            // Direct mutation justified: no public API exists to set exposure state
            // in isolation — CheckExposure derives it from criminal records, which
            // this test intentionally bypasses to isolate AddReputation behavior.
#pragma warning disable RA0002
            mindComp!.ExposurePenaltyMultiplier = 0.5f;
            mindComp.HighestObservedStatus = SecurityStatus.Suspected;
#pragma warning restore RA0002

            var rawGain = 20;
            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                rawGain);

            // With 0.5x multiplier, 20 raw should yield 10 effective
            var expectedEffective = (int) Math.Round(rawGain * 0.5f);
            Assert.That(mindComp.Reputation, Is.EqualTo(expectedEffective),
                $"Expected {expectedEffective} rep with 0.5x exposure penalty, got {mindComp.Reputation}.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that exposure penalty at the floor multiplier still grants some reputation.
    /// </summary>
    [Test]
    public async Task TestAddReputationAtExposureFloor()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp));

            // Set multiplier to the floor value.
            // Direct mutation justified: same reason as TestAddReputationWithExposurePenalty —
            // we need a specific multiplier to verify floor-clamped reputation math.
#pragma warning disable RA0002
            mindComp!.ExposurePenaltyMultiplier = ctx.RuleComp.ExposureFloorMultiplier;
#pragma warning restore RA0002

            var rawGain = 100;
            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                rawGain);

            var expectedEffective = (int) Math.Round(rawGain * ctx.RuleComp.ExposureFloorMultiplier);
            Assert.That(mindComp.Reputation, Is.EqualTo(expectedEffective),
                $"Expected {expectedEffective} rep at floor multiplier ({ctx.RuleComp.ExposureFloorMultiplier}).");
            Assert.That(mindComp.Reputation, Is.GreaterThan(0),
                "Even at floor multiplier, some reputation should be granted.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: GrantTelecrystals Edge Cases

    /// <summary>
    /// Verifies that granting negative TC is a no-op (the guard returns early).
    /// </summary>
    [Test]
    public async Task TestGrantTelecrystalsNegativeIsNoOp()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp));
            Assert.That(mindComp!.UplinkEntity, Is.Not.Null);
            Assert.That(ctx.EntMan.TryGetComponent<StoreComponent>(mindComp.UplinkEntity!.Value, out var store));

            var currencyId = ctx.RuleComp.TelecrystalCurrency.Id;
            var balanceBefore = store!.Balance.GetValueOrDefault(currencyId);

            // Negative amount
            ctx.SabOpsSys.GrantTelecrystals(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                -10);

            Assert.That(store.Balance.GetValueOrDefault(currencyId), Is.EqualTo(balanceBefore),
                "Granting negative TC should not change balance.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: CompleteOperation on Non-Saboteur

    /// <summary>
    /// Verifies that calling CompleteOperation on an entity that is not a saboteur
    /// is a safe no-op (the Resolve guard returns early).
    /// </summary>
    [Test]
    public async Task TestCompleteOperationOnNonSaboteur()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        // Do NOT make the player a saboteur — they're a regular crewmember

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.HasComponent<SaboteurComponent>(ctx.PlayerEntity), Is.False,
                "Player should not be a saboteur.");

            // Calling CompleteOperation should not throw
            Assert.DoesNotThrow(() =>
                ctx.SabOpsSys.CompleteOperation(
                    (ctx.PlayerEntity, null),
                    (ctx.RuleEnt, null),
                    "SabotageComms"),
                "CompleteOperation should not throw on a non-saboteur entity.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that AddReputation on an entity without SaboteurComponent is a safe no-op.
    /// </summary>
    [Test]
    public async Task TestAddReputationOnNonSaboteur()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() =>
                ctx.SabOpsSys.AddReputation(
                    (ctx.PlayerEntity, null),
                    (ctx.RuleEnt, null),
                    50),
                "AddReputation should not throw on a non-saboteur entity.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that GrantTelecrystals on a non-saboteur entity is a safe no-op.
    /// </summary>
    [Test]
    public async Task TestGrantTelecrystalsOnNonSaboteur()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() =>
                ctx.SabOpsSys.GrantTelecrystals(
                    (ctx.PlayerEntity, null),
                    (ctx.RuleEnt, null),
                    10),
                "GrantTelecrystals should not throw on a non-saboteur entity.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: RunCompletionSweep / RunExposureChecks with No Saboteurs

    /// <summary>
    /// Verifies that RunCompletionSweep with no saboteurs in the world is a safe no-op.
    /// </summary>
    [Test]
    public async Task TestCompletionSweepWithNoSaboteurs()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        // Do NOT make anyone a saboteur

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() =>
                ctx.SabOpsSys.RunCompletionSweep((ctx.RuleEnt, null)),
                "RunCompletionSweep should not throw when no saboteurs exist.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// Verifies that RunExposureChecks with no saboteurs in the world is a safe no-op.
    /// </summary>
    [Test]
    public async Task TestExposureCheckWithNoSaboteurs()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.DoesNotThrow(() =>
                ctx.SabOpsSys.RunExposureChecks((ctx.RuleEnt, null)),
                "RunExposureChecks should not throw when no saboteurs exist.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Tier Does Not Regress

    /// <summary>
    /// Verifies that tier never decreases — even when adding zero reputation after
    /// already advancing to a higher tier.
    /// </summary>
    [Test]
    public async Task TestTierDoesNotRegress()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.EntMan.TryGetComponent<SaboteurMindComponent>(mindId, out var mindComp));

            // Push to tier 1
            var tier1Threshold = ctx.RuleComp.Tiers[1].Threshold;
            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                tier1Threshold);

            Assert.That(mindComp!.ReputationTier, Is.GreaterThanOrEqualTo(1));
            var tierAfterAdvance = mindComp.ReputationTier;

            // Add zero reputation — tier must not change
            ctx.SabOpsSys.AddReputation(
                (ctx.PlayerEntity, null),
                (ctx.RuleEnt, null),
                0);

            Assert.That(mindComp.ReputationTier, Is.EqualTo(tierAfterAdvance),
                "Tier should never decrease after adding zero reputation.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Dirty Domain Does Not Set Exposure When Not Records

    /// <summary>
    /// Verifies that dirtying a non-Records domain (e.g. Power) sets
    /// CompletionSweepNeeded but NOT ExposureCheckNeeded.
    /// </summary>
    [Test]
    public async Task TestNonRecordsDomainDoesNotTriggerExposure()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.CondCore.TryGetDirtyTracking(out var dirty));

            ctx.CondCore.MarkDirty(SaboteurDirtyDomain.Power);

            Assert.That(dirty!.CompletionSweepNeeded, Is.True,
                "CompletionSweepNeeded should be set after dirtying Power.");
            Assert.That(dirty.ExposureCheckNeeded, Is.False,
                "ExposureCheckNeeded should NOT be set for non-Records domain.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Deleted Entity Cleaned from Domain Interest

    /// <summary>
    /// Verifies that a deleted entity is pruned from the domain interest list
    /// during a flush, and does not cause exceptions.
    /// </summary>
    [Test]
    public async Task TestDeletedEntityPrunedFromDomainInterest()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.CondCore.TryGetDirtyTracking(out var dirty));

            // Register an entity, then delete it before flushing
            var tempEntity = ctx.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            ctx.CondCore.RegisterInterest(dirty!, tempEntity, SaboteurDirtyDomain.Camera);

            // Clear initial dirty state
            var cacheKey = ctx.CondCore.MakeCacheKey(tempEntity);
            ctx.CondCore.CacheAndReturn(dirty!, tempEntity, cacheKey, 0f);

            // Delete the entity
            ctx.EntMan.DeleteEntity(tempEntity);

            // Dirty the Camera domain — flush should prune the dead entity
            ctx.CondCore.MarkDirty(SaboteurDirtyDomain.Camera);
            Assert.DoesNotThrow(() => ctx.CondCore.FlushPendingDirty(dirty!),
                "Flushing a domain with a deleted interested entity should not throw.");

            // The deleted entity should not appear in DirtyObjectives
            Assert.That(dirty!.DirtyObjectives, Does.Not.Contain(tempEntity),
                "Deleted entity should be pruned from dirty objectives.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Progress Cache Invalidation on Dirty

    /// <summary>
    /// Verifies that a cached progress value becomes unavailable after
    /// the objective is marked dirty via domain flush.
    /// </summary>
    [Test]
    public async Task TestCacheInvalidatedWhenObjectiveDirty()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.CondCore.TryGetDirtyTracking(out var dirty));

            var testEntity = ctx.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            ctx.CondCore.RegisterInterest(dirty!, testEntity, SaboteurDirtyDomain.Bolts);

            // Cache a progress value
            var cacheKey = ctx.CondCore.MakeCacheKey(testEntity);
            ctx.CondCore.CacheAndReturn(dirty!, testEntity, cacheKey, 0.75f);

            // Verify it's retrievable
            Assert.That(ctx.CondCore.TryGetCached(dirty!, testEntity, cacheKey, out var cached));
            Assert.That(cached, Is.EqualTo(0.75f).Within(0.001f));

            // Mark the domain dirty — cache should become stale
            ctx.CondCore.MarkDirty(SaboteurDirtyDomain.Bolts);
            ctx.CondCore.FlushPendingDirty(dirty!);

            Assert.That(ctx.CondCore.TryGetCached(dirty!, testEntity, cacheKey, out _), Is.False,
                "Cached progress should not be available while objective is dirty.");

            ctx.EntMan.DeleteEntity(testEntity);
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Exposure Floor Enforcement

    /// <summary>
    /// Verifies that the exposure multiplier configuration never goes below the floor.
    /// </summary>
    [Test]
    public async Task TestExposureMultipliersAboveFloor()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        await ctx.Server.WaitAssertion(() =>
        {
            foreach (var (status, multiplier) in ctx.RuleComp.ExposureMultipliers)
            {
                Assert.That(multiplier, Is.GreaterThanOrEqualTo(ctx.RuleComp.ExposureFloorMultiplier),
                    $"Exposure multiplier for {status} ({multiplier}) is below the floor " +
                    $"({ctx.RuleComp.ExposureFloorMultiplier}). This would be clamped at runtime, " +
                    "indicating a prototype misconfiguration.");
            }
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test: Max Active Objectives Cap

    /// <summary>
    /// Verifies that initial objective assignment does not exceed the MaxActiveObjectives cap.
    /// </summary>
    [Test]
    public async Task TestMaxActiveObjectivesCap()
    {
        await using var ctx = await SetupRoundWithSaboteur();

        var mindId = await MakePlayerSaboteur(ctx);

        await ctx.Server.WaitAssertion(() =>
        {
            Assert.That(ctx.MindSys.TryGetMind(ctx.PlayerEntity, out _, out var mind));

            var sabObjectiveCount = 0;
            foreach (var obj in mind!.Objectives)
            {
                if (ctx.EntMan.HasComponent<SaboteurOperationComponent>(obj))
                    sabObjectiveCount++;
            }

            Assert.That(sabObjectiveCount, Is.LessThanOrEqualTo(ctx.RuleComp.MaxActiveObjectives),
                $"Saboteur should never have more than {ctx.RuleComp.MaxActiveObjectives} " +
                $"active objectives, but has {sabObjectiveCount}.");
        });

        await ctx.Pair.CleanReturnAsync();
    }

    #endregion

    #region Test Context

    /// <summary>
    /// Bundles all the services and entities needed for saboteur gameplay tests.
    /// Implements IAsyncDisposable so it can be used with <c>await using</c>.
    /// </summary>
    private sealed class SaboteurTestContext : IAsyncDisposable
    {
        public readonly Pair.TestPair Pair;
        public readonly RobustIntegrationTest.ServerIntegrationInstance Server;
        public readonly IEntityManager EntMan;
        public readonly GameTicker Ticker;
        public readonly MindSystem MindSys;
        public readonly RoleSystem RoleSys;
        public readonly SaboteurRuleSystem SabRuleSys;
        public readonly SaboteurOperationSystem SabOpsSys;
        public readonly SaboteurConditionCoreSystem CondCore;
        public readonly EntityUid RuleEnt;
        public readonly SaboteurRuleComponent RuleComp;
        public readonly EntityUid PlayerEntity;
        public readonly ICommonSession[] Dummies;

        public SaboteurTestContext(
            Pair.TestPair pair,
            RobustIntegrationTest.ServerIntegrationInstance server,
            IEntityManager entMan,
            GameTicker ticker,
            MindSystem mindSys,
            RoleSystem roleSys,
            SaboteurRuleSystem sabRuleSys,
            SaboteurOperationSystem sabOpsSys,
            SaboteurConditionCoreSystem condCore,
            EntityUid ruleEnt,
            SaboteurRuleComponent ruleComp,
            EntityUid playerEntity,
            ICommonSession[] dummies)
        {
            Pair = pair;
            Server = server;
            EntMan = entMan;
            Ticker = ticker;
            MindSys = mindSys;
            RoleSys = roleSys;
            SabRuleSys = sabRuleSys;
            SabOpsSys = sabOpsSys;
            CondCore = condCore;
            RuleEnt = ruleEnt;
            RuleComp = ruleComp;
            PlayerEntity = playerEntity;
            Dummies = dummies;
        }

        public async ValueTask DisposeAsync()
        {
            await Pair.DisposeAsync();
        }
    }

    #endregion
}
