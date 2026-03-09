// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using Content.Shared.IdentityManagement;
using Content.Shared.Objectives.Components;
using Content.Shared.Silicons.Laws.Components;
using Content.Shared.Silicons.StationAi;

using Content.Server._Omu.Saboteur.Conditions.Components;
using Content.Server._Omu.Saboteur.Systems;
using Content.Server._Omu.Saboteur.Components;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Evaluates subvert-AI objectives by checking whether silicon law sets
/// have been altered by the saboteur.
/// </summary>
public sealed class SaboteurSubvertAIConditionSystem : EntitySystem
{
    [Dependency] private readonly SaboteurConditionCoreSystem _core = default!;

    /// <summary>
    /// Minimum name length required for the subvert check to succeed.
    /// Names shorter than this (e.g. "a", "AI", "to") are too likely to
    /// appear as common words in default law text, making the objective
    /// trivially completable without ever touching the AI.
    /// </summary>
    private const int MinNameLength = 3;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SaboteurSubvertAIConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<SaboteurSubvertAIConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<SaboteurSubvertAIConditionComponent, RequirementCheckEvent>(OnRequirementCheck);
    }

    private void OnGetProgress(EntityUid uid, SaboteurSubvertAIConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        if (!_core.TryBeginProgressCheck(uid, ref args, out var saboteur, out var dirty))
            return;

        var cacheKey = comp.CacheKey;
        if (_core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return;
        }

        var name = !string.IsNullOrEmpty(comp.SnapshottedName)
            ? comp.SnapshottedName
            : Identity.Name(saboteur, EntityManager);

        if (string.IsNullOrEmpty(name) || name.Length < MinNameLength)
        {
            args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
            return;
        }

        // Use the pre-compiled regex from assignment time when available,
        // falling back to a fresh compilation if the name was resolved late.
        var namePattern = comp.CachedNamePattern
            ?? new Regex(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var query = EntityQueryEnumerator<StationAiHeldComponent, SiliconLawProviderComponent>();
        while (query.MoveNext(out _, out _, out var provider))
        {
            if (provider.Lawset == null)
                continue;

            foreach (var law in provider.Lawset.Laws)
            {
                if (namePattern.IsMatch(law.LawString))
                {
                    args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 1f);
                    return;
                }
            }
        }

        args.Progress = _core.CacheAndReturn(dirty, uid, cacheKey, 0f);
    }

    private void OnAfterAssign(EntityUid uid, SaboteurSubvertAIConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        if (!_core.TryGetDirtyTracking(out var dirty))
            return;

        comp.CacheKey = _core.MakeCacheKey(uid);
        if (args.Mind.OwnedEntity.HasValue)
        {
            comp.SnapshottedName = Identity.Name(args.Mind.OwnedEntity.Value, EntityManager);

            // Pre-compile the whole-word regex at assignment time so progress
            // checks never pay the regex compilation cost.
            if (comp.SnapshottedName.Length >= MinNameLength)
            {
                comp.CachedNamePattern = new Regex(
                    @"\b" + Regex.Escape(comp.SnapshottedName) + @"\b",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
        }
        _core.RegisterInterest(dirty, uid, SaboteurDirtyDomain.SiliconLaw);
    }

    private void OnRequirementCheck(EntityUid uid, SaboteurSubvertAIConditionComponent comp, ref RequirementCheckEvent args)
    {
        if (args.Cancelled)
            return;

        var query = EntityQueryEnumerator<StationAiHeldComponent>();
        if (!query.MoveNext(out _, out _))
            args.Cancelled = true;
    }
}
