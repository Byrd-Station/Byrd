using System.Linq;
using Content.Server.Administration;
using Content.Server.Preferences.Managers;
using Content.Shared.Administration;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Omu.Server.Administration.Commands;

[AdminCommand(AdminFlags.Logs)]
public sealed class LsTraits : LocalizedCommands
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IServerPreferencesManager _prefsManager = default!;

    public override string Command => "lstraits";
    public override string Description => "Lists the traits of a specified player.";
    public override string Help => "Usage: lstraits <Username>";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        ICommonSession? session;
        if (args.Length > 0)
            _players.TryGetSessionByUsername(args[0], out session);
        else
            session = shell.Player;

        if (session == null)
        {
            shell.WriteError(LocalizationManager.GetString("shell-target-player-does-not-exist"));
            return;
        }

        // Get the player's preferences
        if (!_prefsManager.TryGetCachedPreferences(session.UserId, out var prefs))
        {
            shell.WriteError("Could not find player preferences. Have they fully connected?");
            return;
        }

        // Get the selected character profile
        if (prefs.SelectedCharacter is not HumanoidCharacterProfile character)
        {
            shell.WriteError("Could not find character profile.");
            return;
        }

        shell.WriteLine($"Traits for {character.Name} (User: {session.Name}):");

        // Get the player's traits
        var traits = character.TraitPreferences;
        if (!traits.Any())
        {
            shell.WriteLine("No traits found.");
            return;
        }

        // Display each trait with its name
        foreach (var traitId in traits)
        {
            shell.WriteLine(_prototypeManager.TryIndex(traitId, out _)
                ? $"  - {traitId}"
                : $"  - Unknown trait: {traitId}");
        }
    }


    // Localized Commands auto complete
    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                CompletionHelper.SessionNames(),
                LocalizationManager.GetString("shell-argument-username-hint"));
        }

        return CompletionResult.Empty;
    }
}
