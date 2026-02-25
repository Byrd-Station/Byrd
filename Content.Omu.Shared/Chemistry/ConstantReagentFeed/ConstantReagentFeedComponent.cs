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

    public float Accumulator;
}
