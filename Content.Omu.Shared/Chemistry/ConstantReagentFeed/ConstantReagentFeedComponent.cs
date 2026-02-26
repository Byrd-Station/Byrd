// SPDX-FileCopyrightText: 2026 Eponymic-sys
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.Goobstation.Maths.FixedPoint;

namespace Content.Omu.Shared.Chemistry.ConstantReagentFeed;

[RegisterComponent]
public sealed partial class ConstantReagentFeedComponent : Component
{
    /// Reagents added per second to the injectable solution.
    [DataField]
    public Dictionary<string, FixedPoint2> ReagentsPerSecond = new();

    /// How often the injection tick occurs, in seconds.
    [DataField]
    public float UpdateInterval = 1f;

    /// Optional entity to pull reagents from instead of creating them.
    /// When null, reagents are generated from nothing (original behaviour).
    [DataField]
    public EntityUid? SourceEntity;

    /// Named solution on the source entity to draw from.
    /// Only used when <see cref="SourceEntity"/> is set.
    /// When null the system falls back to the drawable solution on the source.
    [DataField]
    public string? SourceSolutionName;

    public float Accumulator;
}
