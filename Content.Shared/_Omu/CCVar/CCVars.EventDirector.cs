using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Enables Omu's event director by default so live test rounds use the new scheduler path
    ///     instead of the legacy station event schedulers.
    /// </summary>
    public static readonly CVarDef<bool>
        EventDirectorEnabled = CVarDef.Create("event_director.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Selects which event director config prototype should drive the round.
    /// </summary>
    public static readonly CVarDef<string>
        EventDirectorConfig = CVarDef.Create("event_director.config", "OmuDefault", CVar.SERVERONLY);
}
