// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.GameObjects;

using Content.Server._Omu.Saboteur.Systems;

namespace Content.Server._Omu.Saboteur.Components;

/// <summary>
/// Tag component applied to a saboteur entity (the body, not the mind).
/// </summary>
[RegisterComponent, Access(typeof(SaboteurRuleSystem))]
public sealed partial class SaboteurComponent : Component;
