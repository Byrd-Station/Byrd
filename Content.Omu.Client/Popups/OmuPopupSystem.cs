// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.Popups;
using Content.Omu.Shared.Popups;

namespace Content.Omu.Client.Popups;

/// <summary>
///     handles PopupEntityNoChatEvent - shows the popup above the entity
///     like a normal entity popup, but skips the chat log entirely.
///     used for species communication cues (e.g. resomi chirp direction).
/// </summary>
public sealed class OmuPopupSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeNetworkEvent<PopupEntityNoChatEvent>(OnPopupEntityNoChat);
    }

    private void OnPopupEntityNoChat(PopupEntityNoChatEvent ev)
    {
        var entity = GetEntity(ev.Uid);
        _popup.PopupEntityNoChat(ev.Message, entity, ev.Type);
    }
}
