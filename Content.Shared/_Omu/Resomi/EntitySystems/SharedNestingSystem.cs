// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Actions;

namespace Content.Shared._Omu.Resomi.EntitySystems;

/// <summary>
/// Allows Resomi to curl up and rest in a nest, healing slowly while immobile.
/// </summary>
public abstract class SharedNestingSystem : EntitySystem;

/// <summary>
/// Fired when the Resomi uses the Enter Nest action.
/// </summary>
public sealed partial class EnterNestActionEvent : InstantActionEvent;

/// <summary>
/// Fired when the Resomi uses the Exit Nest action.
/// </summary>
public sealed partial class ExitNestActionEvent : InstantActionEvent;