// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Omu.Shared.Popups;

/// <summary>
///     like PopupEntityEvent but the client will not log it to the chat panel,
///     even if the chat.log_in_chat cvar is on. used for species communication
///     cues (e.g. resomi chirp) that should float above the entity but stay out of chat.
/// </summary>
[Serializable, NetSerializable]
public sealed class PopupEntityNoChatEvent : PopupEvent
{
    public NetEntity Uid { get; }

    public PopupEntityNoChatEvent(string message, PopupType type, NetEntity uid) : base(message, type)
    {
        Uid = uid;
    }
}
