// SPDX-FileCopyrightText: 2026 Raze500
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables Omu's event director. when true, BasicStationEventScheduler and
    ///     RampingStationEventScheduler yield so the director is the only active scheduler.
    ///     disabled by default — host enables it via server config.
    /// </summary>
    public static readonly CVarDef<bool>
        EventDirectorEnabled = CVarDef.Create("event_director.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Selects which eventDirectorConfig prototype drives the round.
    ///     change this via 'eventdirector setconfig id' in the admin console.
    /// </summary>
    public static readonly CVarDef<string>
        EventDirectorConfig = CVarDef.Create("event_director.config", "OmuDefault", CVar.SERVERONLY);
}
