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
///
/// <remarks>
///     HOW THIS SYSTEM WORKS
///
///     the problem: SS14 has a cvar called chat.log_in_chat (default on) that makes every
///     entity popup also appear as a line in the chat panel. for most things that is fine,
///     but species communication cues should be subtle - visible in the world but not spamming chat.
///
///     normal popup flow (PopupEntityEvent):
///         server raises event -> client receives it -> PopupMessage() shows it above the entity
///         -> if LogInChat is on, it ALSO gets copied to the chat panel. not what we want.
///
///     this system flow (PopupEntityNoChatEvent):
///         server raises event -> client OmuPopupSystem receives it -> PopupSystem.PopupEntityNoChat()
///         shows it above the entity with skipChatLog=true -> the chat copy step is skipped entirely.
///         the popup appears in the world exactly like a normal popup, just never in chat history.
///
///     HOW TO USE IT FROM A SERVER SYSTEM
///
///         using Content.Omu.Shared.Popups;
///         using Robust.Server.GameObjects;
///
///         if (TryComp(recipient, out ActorComponent? actor))
///             RaiseNetworkEvent(
///                 new PopupEntityNoChatEvent("your message", PopupType.Large, GetNetEntity(uid)),
///                 actor.PlayerSession
///             );
///
///     uid   = the entity the popup floats above (usually the same as recipient)
///     recipient = the player who will see it (actor.PlayerSession)
/// </remarks>
[Serializable, NetSerializable]
public sealed class PopupEntityNoChatEvent : PopupEvent
{
    public NetEntity Uid { get; }

    public PopupEntityNoChatEvent(string message, PopupType type, NetEntity uid) : base(message, type)
    {
        Uid = uid;
    }
}
