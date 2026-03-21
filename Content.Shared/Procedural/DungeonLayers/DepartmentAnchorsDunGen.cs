using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.DungeonLayers;

/// <summary>
/// Places fixed departmental anchor entities into a generated ship.
/// Seven placement zones: bow, portBow, starboardBow, mid, portStern, starboardStern, stern.
/// </summary>
public sealed partial class DepartmentAnchorsDunGen : IDunGenLayer
{
    /// <summary>
    /// Anchors placed in the front section of the ship.
    /// </summary>
    [DataField]
    public List<EntProtoId> BowAnchors = new();

    /// <summary>
    /// Anchors placed forward-left (port bow).
    /// </summary>
    [DataField]
    public List<EntProtoId> PortBowAnchors = new();

    /// <summary>
    /// Anchors placed forward-right (starboard bow).
    /// </summary>
    [DataField]
    public List<EntProtoId> StarboardBowAnchors = new();

    /// <summary>
    /// Anchors placed around the center section of the ship.
    /// </summary>
    [DataField]
    public List<EntProtoId> MidAnchors = new();

    /// <summary>
    /// Anchors placed aft-left (port stern).
    /// </summary>
    [DataField]
    public List<EntProtoId> PortSternAnchors = new();

    /// <summary>
    /// Anchors placed aft-right (starboard stern).
    /// </summary>
    [DataField]
    public List<EntProtoId> StarboardSternAnchors = new();

    /// <summary>
    /// Anchors placed in the rear section of the ship.
    /// </summary>
    [DataField]
    public List<EntProtoId> SternAnchors = new();
}
