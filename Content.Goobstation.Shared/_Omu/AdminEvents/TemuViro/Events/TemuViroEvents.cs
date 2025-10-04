// SPDX-FileCopyrightText: 2025 RichardBlonski <48651647+RichardBlonski@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Goobstation.Shared._Omu.AdminEvents.TemuViro.Events;


[ByRefEvent]
public readonly record struct OnVomitEvent(NetEntity Entity);

[ByRefEvent]
public readonly record struct OnCuredEvent(NetEntity Entity, bool ShowPopup = false);

