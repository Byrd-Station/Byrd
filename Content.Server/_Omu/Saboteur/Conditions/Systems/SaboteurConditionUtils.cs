// SPDX-FileCopyrightText: 2026 Eponymic-sys
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.GameObjects;
using Content.Shared.Objectives.Components;
using Content.Server._Omu.Saboteur.Components;
using Content.Server._Omu.Saboteur.Systems;

namespace Content.Server._Omu.Saboteur.Conditions.Systems;

/// <summary>
/// Provides shared utility methods for Saboteur condition progress calculation and caching.
/// </summary>
public static class SaboteurConditionUtils
{
    /// <summary>
    /// Assigns progress and caches the result if dirty is not null.
    /// </summary>
    public static void AssignProgressIfNotNull(
        SaboteurConditionCoreSystem core,
        SaboteurDirtyTrackingComponent? dirty,
        EntityUid uid,
        string cacheKey,
        ref ObjectiveGetProgressEvent args,
        float progress)
    {
        if (dirty != null)
        {
            AssignProgress(core, dirty, uid, cacheKey, ref args, progress);
        }
        // else: dirty is null, so skip assigning progress or handle as needed
    }
    /// <summary>
    /// Standardized progress check and caching pattern for Saboteur conditions.
    /// </summary>
    /// <returns>True if progress was assigned and further logic should return.</returns>
    public static bool TryStandardProgress(
        SaboteurConditionCoreSystem core,
        EntityUid uid,
        ref ObjectiveGetProgressEvent args,
        string cacheKey,
        out EntityUid saboteur,
        out SaboteurDirtyTrackingComponent? dirty,
        float? emptyProgress = 0f)
    {
        saboteur = default;
        dirty = null;
        if (!core.TryBeginProgressCheck(uid, ref args, out saboteur, out dirty))
            return true;
        if (dirty != null && core.TryGetCached(dirty, uid, cacheKey, out var cached))
        {
            args.Progress = cached;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Assigns progress and caches the result.
    /// </summary>
    public static void AssignProgress(
        SaboteurConditionCoreSystem core,
        SaboteurDirtyTrackingComponent dirty,
        EntityUid uid,
        string cacheKey,
        ref ObjectiveGetProgressEvent args,
        float progress)
    {
        args.Progress = core.CacheAndReturn(dirty, uid, cacheKey, progress);
    }
}
