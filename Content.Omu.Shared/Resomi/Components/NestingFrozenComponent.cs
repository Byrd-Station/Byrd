// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Omu.Shared.Resomi.Components;

/// <summary>
/// Added to a Resomi when it enters nesting state.
/// Prevents movement and most actions, but allows speech, emotes, and the exit-nest action.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NestingFrozenComponent : Component
{
}

/// <summary>
/// Added to an action entity to mark it as usable while nesting.
/// SharedNestingFrozenSystem will allow UseAttemptEvent through for any action
/// that has this component, so the holder can activate it from the nest.
/// </summary>
[RegisterComponent]
public sealed partial class AllowedWhileNestingComponent : Component
{
}
