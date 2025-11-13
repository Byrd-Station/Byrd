using Content.Omu.Server.RulesSmite.UI;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Omu.Server.Administration.Systems;

public sealed partial class OmuAdminVerbSystem
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private void AddSmiteVerbs(GetVerbsEvent<Verb> args)
    {
        if (!SmitesAllowed(args))
            return;

        if (_player.TryGetSessionByEntity(args.Target, out var session))
        {
            var rulesName = Loc.GetString("admin-smite-rules-name").ToLowerInvariant();
            Verb rules = new()
            {
                Text = rulesName,
                Category = VerbCategory.Smite,
                Icon = new SpriteSpecifier.Rsi(new ("Mobs/Species/Human/organs.rsi"), "brain"),
                Act = () =>
                {
                    _eui.OpenEui(new RulesSmiteEui(), session);
                },
                Impact = LogImpact.Extreme,
                Message = string.Join(": ", rulesName, Loc.GetString("admin-smite-rules-description"))
            };
            args.Verbs.Add(rules);
        }
    }

    private bool SmitesAllowed(GetVerbsEvent<Verb> args)
    {
        if (!TryComp(args.User, out ActorComponent? actor) ||
            !_admin.HasAdminFlag(actor.PlayerSession, AdminFlags.Fun) ||
            HasComp<MapComponent>(args.Target) || HasComp<MapGridComponent>(args.Target))
            return false;

        return true;
    }
}
