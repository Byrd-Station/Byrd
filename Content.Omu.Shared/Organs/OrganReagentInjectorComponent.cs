// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Chemistry.Components;

namespace Content.Omu.Shared.Organs;

/// <summary>
/// Injects reagents into host body's solution at regular intervals.
/// </summary>
[RegisterComponent]
public sealed partial class OrganReagentInjectorComponent : Component
{
    [DataField]
    public string TargetSolution = "chemicals";

    [DataField(required: true)]
    public Solution Reagents = default!;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1);

    [ViewVariables]
    public TimeSpan NextInjectTime;
}
