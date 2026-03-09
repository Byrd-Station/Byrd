// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Omu.Saboteur;

/// <summary>
/// Defines a unique saboteur operation (objective type). Each prototype has
/// an ID and a localization key used to set the objective's name and description.
/// </summary>
[Prototype]
public sealed partial class SaboteurOperationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Base localization ID; the description is derived as <c>{LocId}-desc</c>.
    /// </summary>
    [DataField(required: true)]
    public string LocId { get; private set; } = default!;
}
