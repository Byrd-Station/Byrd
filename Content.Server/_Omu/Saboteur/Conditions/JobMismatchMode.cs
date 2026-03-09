// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Omu.Saboteur.Conditions;

/// <summary>
/// Specifies how job mismatches are detected for
/// <see cref="Components.SaboteurJobMismatchConditionComponent"/>.
/// </summary>
public enum JobMismatchMode
{
    /// <summary>
    /// Any difference between the original and current job title counts as a mismatch.
    /// </summary>
    AnyDifference,

    /// <summary>
    /// Only counts as a mismatch when a formerly command-level job title has been
    /// changed to a non-command role.
    /// </summary>
    DemotedFromCommand,
}
