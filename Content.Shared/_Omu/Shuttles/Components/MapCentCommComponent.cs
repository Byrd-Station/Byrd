using Robust.Shared.GameStates;

namespace Content.Shared._Omu.Shuttles.Components;

/// <summary>
/// Marker component for the CentComm map.
/// Used for CentComm's FTL whitelist.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MapCentcommComponent : Component;