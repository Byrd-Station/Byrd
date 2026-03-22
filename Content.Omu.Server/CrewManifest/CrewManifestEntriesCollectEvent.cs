using Content.Shared.CrewManifest;
using Content.Shared.Roles;

namespace Content.Omu.Server.CrewManifest;

/// <summary>
/// Allows Omu systems to append extra crew manifest rows before the final sort/cache step.
/// </summary>
public readonly record struct CrewManifestEntriesCollectEvent(
    EntityUid Station,
    List<(JobPrototype? job, CrewManifestEntry entry)> Entries);
