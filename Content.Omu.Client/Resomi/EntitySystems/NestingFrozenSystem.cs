using Content.Omu.Shared.Resomi.EntitySystems;

namespace Content.Omu.Client.Resomi.EntitySystems;

/// <summary>
/// Client-side concrete implementation of SharedNestingFrozenSystem.
/// Required so movement blocking applies during client-side prediction.
/// </summary>
public sealed class NestingFrozenSystem : SharedNestingFrozenSystem;
