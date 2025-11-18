using Robust.Shared.Configuration;

namespace Content.Omu.Common.CCVar;

[CVarDefs]
public sealed partial class OmuCVars
{
    /// <summary>
    ///     How many seconds a player will have to wait to close the rules smite window.
    /// </summary>
    public static readonly CVarDef<float> RulesSmiteTime =
        CVarDef.Create("rulessmite.time", 60f, CVar.SERVER | CVar.REPLICATED);
}
