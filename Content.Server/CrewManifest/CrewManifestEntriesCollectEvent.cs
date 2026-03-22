using Content.Shared.CrewManifest;
using Content.Shared.Roles;

namespace Content.Server.CrewManifest;

/// <summary>
/// Allows fork modules to append extra crew manifest rows before the final sort/cache step.
/// </summary>
public readonly record struct CrewManifestEntriesCollectEvent(
    EntityUid Station,
    List<(JobPrototype? job, CrewManifestEntry entry)> Entries);
