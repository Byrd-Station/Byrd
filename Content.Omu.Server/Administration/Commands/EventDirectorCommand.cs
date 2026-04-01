using System.Linq;
using Content.Server.Administration;
using Content.Omu.Server.GameTicking.EventDirector;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Omu.Server.Administration.Commands;

// IConsoleCommand uses IoC injection ( which only knows about registered services)
// EntitySystems are not IoC services and they live in IEntitySystemManager
// so we inject that and resolve EventDirectorSystem at call time instead
[AdminCommand(AdminFlags.Debug)]
public sealed class EventDirectorCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _entitySystems = default!;

    private EventDirectorSystem Director => _entitySystems.GetEntitySystem<EventDirectorSystem>();

    public string Command => "eventdirector";
    public string Description => "Inspect and control Omu's event director.";
    public string Help => "eventdirector status | history | list <roundstart|minor|midround|timer> | roll <roundstart|minor|midround|timer> | fire <ruleId> | setconfig <configId> | pause | resume";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "status":
                shell.WriteLine(Director.BuildStatus());
                return;

            case "history":
                var history = Director.GetHistory();
                if (history.Count == 0)
                {
                    shell.WriteLine("no events fired by the director this round yet.");
                    return;
                }
                foreach (var (time, rule, table) in history)
                {
                    shell.WriteLine($"[{time:hh\\:mm\\:ss}] {rule} ({table})");
                }
                return;

            case "list":
                if (args.Length < 2)
                {
                    shell.WriteError("Usage: eventdirector list <roundstart|minor|midround|timer>");
                    return;
                }

                foreach (var line in Director.DescribeTable(args[1]))
                {
                    shell.WriteLine(line);
                }
                return;

            case "roll":
                if (args.Length < 2)
                {
                    shell.WriteError("Usage: eventdirector roll <roundstart|minor|midround|timer>");
                    return;
                }

                Director.RollNamedTable(args[1], out var rollMessage);
                shell.WriteLine(rollMessage);
                return;

            case "fire":
                if (args.Length < 2)
                {
                    shell.WriteError("Usage: eventdirector fire <ruleId>");
                    return;
                }

                Director.FireRule(args[1], out var fireMessage);
                shell.WriteLine(fireMessage);
                return;

            case "setconfig":
                if (args.Length < 2)
                {
                    shell.WriteError("Usage: eventdirector setconfig <configId>");
                    return;
                }

                Director.SetConfig(args[1], out var configMessage);
                shell.WriteLine(configMessage);
                return;

            case "pause":
                Director.Pause();
                shell.WriteLine("Event director paused.");
                return;

            case "resume":
                Director.Resume();
                shell.WriteLine("Event director resumed.");
                return;

            default:
                shell.WriteError($"Unknown subcommand '{args[0]}'.");
                shell.WriteLine(Help);
                return;
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                new[]
                {
                    new CompletionOption("status"),
                    new CompletionOption("history"),
                    new CompletionOption("list"),
                    new CompletionOption("roll"),
                    new CompletionOption("fire"),
                    new CompletionOption("setconfig"),
                    new CompletionOption("pause"),
                    new CompletionOption("resume"),
                },
                "<subcommand>");
        }

        if (args.Length == 2 && (args[0].Equals("list", StringComparison.OrdinalIgnoreCase) || args[0].Equals("roll", StringComparison.OrdinalIgnoreCase)))
        {
            return CompletionResult.FromHintOptions(
                new[]
                {
                    new CompletionOption("roundstart"),
                    new CompletionOption("minor"),
                    new CompletionOption("midround"),
                    new CompletionOption("timer"),
                },
                "<table>");
        }

        if (args.Length == 2 && args[0].Equals("setconfig", StringComparison.OrdinalIgnoreCase))
        {
            // suggest all known eventDirectorConfig prototype ids
            var proto = IoCManager.Resolve<IPrototypeManager>();
            var options = proto.EnumeratePrototypes<EventDirectorConfigPrototype>()
                .Select(p => new CompletionOption(p.ID))
                .ToArray();
            return CompletionResult.FromHintOptions(options, "<configId>");
        }

        return CompletionResult.Empty;
    }
}
