using Content.Client.Eui;
using Content.Omu.Common.CCVar;
using Content.Shared.Eui;
using Robust.Shared.Configuration;

namespace Content.Omu.Client.RulesSmite.UI;

public sealed class RulesSmiteEui : BaseEui
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly RulesSmiteWindow _window;

    public RulesSmiteEui()
    {
        _window = new RulesSmiteWindow(_cfg.GetCVar(OmuCVars.RulesSmiteTime));

        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnAcceptPressed += () => _window.Close();
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }
}
