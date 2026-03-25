// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;
using Robust.Shared.Map;

namespace Content.Shared._Omu.Resomi.Events;

[Serializable, NetSerializable]
public enum NestingAnimationType
{
    Enter,
    Exit
}

[Serializable, NetSerializable]
public sealed class NestingAnimationEvent : EntityEventArgs
{
    public NetEntity Entity;
    public NetCoordinates Coordinates;
    public NestingAnimationType AnimationType;

    public NestingAnimationEvent(NetEntity entity, NetCoordinates coordinates, NestingAnimationType animationType)
    {
        Entity = entity;
        Coordinates = coordinates;
        AnimationType = animationType;
    }
}