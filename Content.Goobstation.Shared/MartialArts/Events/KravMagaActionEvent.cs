// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aidenkrz <aiden@djkraz.com>
// SPDX-FileCopyrightText: 2025 Lincoln McQueen <lincoln.mcqueen@gmail.com>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;
using Content.Goobstation.Shared.MartialArts.Components;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.MartialArts.Events;

/// <summary>
/// This handles selecting your krav maga action
/// </summary>
public sealed partial class KravMagaActionEvent : InstantActionEvent
{
};

/// <summary>
/// This is for inflicting lung damage
/// </summary>
[Serializable, NetSerializable]
public sealed partial class KravMagaLungPunchEventOld : EntityEventArgs
{
    [DataField("target")]
    public NetEntity Target { get; }

    [DataField("lungDamage")]
    public FixedPoint2 LungDamage { get; }

    public KravMagaLungPunchEventOld(NetEntity target, int lungDamage)
    {
        Target = target;
        LungDamage = lungDamage;
    }

};

[ByRefEvent]
public record struct KravMagaLungPunchEvent(NetEntity Target, FixedPoint2 LungDamage);
