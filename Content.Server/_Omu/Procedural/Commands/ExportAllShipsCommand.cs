using Content.Server.Administration;
using Content.Server.Procedural;
using Content.Shared._Omu.Procedural;
using Content.Shared.Administration;
using Content.Shared.Procedural;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Omu.Procedural.Commands;

/// <summary>
/// Generates all procedural ship variants (or a filtered subset) and exports each as a .yml grid file.
/// Files are written to the user data directory under /ShipExports/.
/// </summary>
[AdminCommand(AdminFlags.Host)]
public sealed class ExportAllShipsCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public string Command => "exportallships";
    public string Description => "Generates all procedural ship configs and exports each as a .yml grid file.";
    public string Help => "Usage: exportallships [prefix] [variantsPerConfig] [startSeed]\n"
        + "  prefix: filter configs by ID prefix, e.g. 'MediumShip' (default: all Ship configs)\n"
        + "  variantsPerConfig: number of variants to generate per config (default: 3)\n"
        + "  startSeed: starting seed value (default: 1)";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var maps = _entManager.System<SharedMapSystem>();
        var dungeon = _entManager.System<DungeonSystem>();
        var mapLoader = _entManager.System<MapLoaderSystem>();

        // Parse arguments.
        var prefix = args.Length >= 1 ? args[0] : "";

        var variantsPerConfig = 3;
        if (args.Length >= 2 && !int.TryParse(args[1], out variantsPerConfig))
        {
            shell.WriteError("Invalid variantsPerConfig value.");
            return;
        }

        if (variantsPerConfig < 1 || variantsPerConfig > 100)
        {
            shell.WriteError("variantsPerConfig must be between 1 and 100.");
            return;
        }

        var startSeed = 1;
        if (args.Length >= 3 && !int.TryParse(args[2], out startSeed))
        {
            shell.WriteError("Invalid startSeed value.");
            return;
        }

        // Collect matching ship configs.
        var configs = new List<(string Id, DungeonConfig Config)>();

        foreach (var proto in _prototype.EnumeratePrototypes<ShipConfigPrototype>())
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                if (proto.ID.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    configs.Add((proto.ID, proto.ToDungeonConfig()));
            }
            else
            {
                if (proto.ID.Contains("Ship", StringComparison.OrdinalIgnoreCase))
                    configs.Add((proto.ID, proto.ToDungeonConfig()));
            }
        }

        foreach (var proto in _prototype.EnumeratePrototypes<DungeonConfigPrototype>())
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                if (proto.ID.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    configs.Add((proto.ID, proto));
            }
            else
            {
                if (proto.ID.Contains("Ship", StringComparison.OrdinalIgnoreCase))
                    configs.Add((proto.ID, proto));
            }
        }

        if (configs.Count == 0)
        {
            shell.WriteError("No matching ship configs found.");
            return;
        }

        configs.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));

        var totalCount = configs.Count * variantsPerConfig;
        shell.WriteLine($"Exporting {totalCount} ships ({configs.Count} configs x {variantsPerConfig} variants)...");

        var exported = 0;
        var failed = 0;

        foreach (var (configId, config) in configs)
        {
            for (var i = 0; i < variantsPerConfig; i++)
            {
                var seed = startSeed + i;
                var fileName = $"ShipExports/{configId}_seed{seed}.yml";

                // Create a temporary map and grid for generation.
                var tempMapUid = maps.CreateMap(out var tempMap);
                var gridUid = _entManager.CreateEntityUninitialized(null,
                    new EntityCoordinates(tempMapUid, 0f, 0f));
                var grid = _entManager.AddComponent<MapGridComponent>(gridUid);
                _entManager.InitializeAndStartEntity(gridUid, tempMap);

                try
                {
                    // Generate the dungeon and wait for it to complete.
                    await dungeon.GenerateDungeonAsync(config, gridUid, grid, Vector2i.Zero, seed);

                    // Save the grid to file.
                    var path = new ResPath("/") / fileName;
                    if (mapLoader.TrySaveGrid(gridUid, path))
                    {
                        exported++;
                        shell.WriteLine($"  [{exported + failed}/{totalCount}] Exported: {fileName}");
                    }
                    else
                    {
                        failed++;
                        shell.WriteError($"  [{exported + failed}/{totalCount}] Failed to save: {fileName}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    shell.WriteError($"  [{exported + failed}/{totalCount}] Error generating {configId} seed {seed}: {ex.Message}");
                }
                finally
                {
                    // Clean up the temporary map (deletes grid too).
                    maps.DeleteMap(tempMap);
                }
            }
        }

        shell.WriteLine($"Export complete. {exported} succeeded, {failed} failed.");
        shell.WriteLine("Files saved to user data directory under ShipExports/.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var prefixes = new List<string> { "", "SmallShip", "MediumShip" };
            foreach (var proto in _prototype.EnumeratePrototypes<ShipConfigPrototype>())
            {
                if (proto.ID.Contains("Ship", StringComparison.OrdinalIgnoreCase))
                    prefixes.Add(proto.ID);
            }
            foreach (var proto in _prototype.EnumeratePrototypes<DungeonConfigPrototype>())
            {
                if (proto.ID.Contains("Ship", StringComparison.OrdinalIgnoreCase))
                    prefixes.Add(proto.ID);
            }

            return CompletionResult.FromHintOptions(prefixes, "Config ID prefix filter (blank = all ships)");
        }

        if (args.Length == 2)
            return CompletionResult.FromHint("Variants per config (default: 3)");

        if (args.Length == 3)
            return CompletionResult.FromHint("Starting seed (default: 1)");

        return CompletionResult.Empty;
    }
}
