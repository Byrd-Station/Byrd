using Content.Shared._Omu.Resomi.EntitySystems;

namespace Content.Client._Omu.Resomi.EntitySystems;

/// <summary>
/// Client-side concrete implementation of SharedNestingFrozenSystem.
/// Required so movement blocking applies during client-side prediction.
/// </summary>
public sealed class NestingFrozenSystem : SharedNestingFrozenSystem;
