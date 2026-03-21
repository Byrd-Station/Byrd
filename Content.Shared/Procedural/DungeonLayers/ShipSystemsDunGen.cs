using Content.Shared.EntityTable;
using Content.Shared.Maps;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.DungeonLayers;

/// <summary>
/// Places essential ship systems on an existing dungeon to make a functional flyable ship.
/// Spawns thrusters around the hull boundary, a pilot console, gyroscope, power generation,
/// atmosphere system, gravity generator, and APC.
/// Should run after hull walls are placed (BoundaryWallDunGen).
/// </summary>
public sealed partial class ShipSystemsDunGen : IDunGenLayer
{
    // --- Propulsion ---

    /// <summary>
    /// Thruster entity spawned at hull boundary facing outward.
    /// </summary>
    [DataField]
    public EntProtoId Thruster = "Thruster";

    /// <summary>
    /// Convenience shortcut: when non-zero, overrides stern/bow/port/starboard thruster counts
    /// to this value at generation time.
    /// </summary>
    [DataField]
    public int ThrustersPerDirection;

    /// <summary>
    /// Number of thrusters on the stern (south) edge - main thrust.
    /// </summary>
    [DataField]
    public int ThrustersStern = 3;

    /// <summary>
    /// Number of thrusters on the bow (north) edge.
    /// </summary>
    [DataField]
    public int ThrustersBow = 1;

    /// <summary>
    /// Number of thrusters on the port (west) edge.
    /// </summary>
    [DataField]
    public int ThrustersPort = 1;

    /// <summary>
    /// Number of thrusters on the starboard (east) edge.
    /// </summary>
    [DataField]
    public int ThrustersStarboard = 1;

    /// <summary>
    /// Gyroscope entity.
    /// </summary>
    [DataField]
    public EntProtoId Gyroscope = "Gyroscope";

    // --- Control ---

    /// <summary>
    /// Shuttle console for piloting.
    /// </summary>
    [DataField]
    public EntProtoId ShuttleConsole = "ComputerShuttle";

    /// <summary>
    /// Pilot seat placed in front of the console.
    /// </summary>
    [DataField]
    public EntProtoId PilotSeat = "ChairPilotSeat";

    // --- Power ---

    /// <summary>
    /// Portable generator for ship power.
    /// </summary>
    [DataField]
    public EntProtoId Generator = "PortableGeneratorPacman";

    /// <summary>
    /// When set, places a 3x3 ring of this entity around the generator (for AME shielding).
    /// Leave null for ships that use a simple portable generator.
    /// </summary>
    [DataField]
    public EntProtoId? GeneratorShielding;

    /// <summary>
    /// Fuel jar entity placed inside the AME controller on spawn.
    /// Only used when <see cref="GeneratorShielding"/> is set.
    /// </summary>
    [DataField]
    public EntProtoId? GeneratorFuel;

    /// <summary>
    /// Substation for power distribution.
    /// </summary>
    [DataField]
    public EntProtoId Substation = "SubstationWallBasic";

    /// <summary>
    /// APC for power delivery to devices.
    /// </summary>
    [DataField]
    public EntProtoId Apc = "APCBasic";

    /// <summary>
    /// SMES battery for energy storage between generator and substation.
    /// </summary>
    [DataField]
    public EntProtoId Smes = "SMESBasic";

    /// <summary>
    /// Cable terminal placed under the SMES to connect it to the HV network.
    /// </summary>
    [DataField]
    public EntProtoId CableTerminal = "CableTerminal";

    /// <summary>
    /// Power cell recharger placed near the engineering area.
    /// </summary>
    [DataField]
    public EntProtoId Recharger = "PowerCellRecharger";

    /// <summary>
    /// HV cable entity.
    /// </summary>
    [DataField]
    public EntProtoId CableHV = "CableHV";

    /// <summary>
    /// MV cable entity.
    /// </summary>
    [DataField]
    public EntProtoId CableMV = "CableMV";

    /// <summary>
    /// APC extension cable entity.
    /// </summary>
    [DataField]
    public EntProtoId CableApc = "CableApcExtension";

    // --- Atmosphere ---

    /// <summary>
    /// Gas vent pump for interior pressurization.
    /// </summary>
    [DataField]
    public EntProtoId VentPump = "GasVentPump";

    /// <summary>
    /// Gas scrubber for CO2 removal.
    /// </summary>
    [DataField]
    public EntProtoId VentScrubber = "GasVentScrubber";

    /// <summary>
    /// Gas pipe straight section.
    /// </summary>
    [DataField]
    public EntProtoId PipeStraight = "GasPipeStraight";

    /// <summary>
    /// Gas pipe bend (90-degree corner).
    /// </summary>
    [DataField]
    public EntProtoId PipeBend = "GasPipeBend";

    /// <summary>
    /// Gas pipe T-junction (three-way).
    /// </summary>
    [DataField]
    public EntProtoId PipeTJunction = "GasPipeTJunction";

    /// <summary>
    /// Gas pipe four-way junction.
    /// </summary>
    [DataField]
    public EntProtoId PipeFourway = "GasPipeFourway";

    /// <summary>
    /// Oxygen canister.
    /// </summary>
    [DataField]
    public EntProtoId OxygenCanister = "OxygenCanister";

    /// <summary>
    /// Nitrogen canister.
    /// </summary>
    [DataField]
    public EntProtoId NitrogenCanister = "NitrogenCanister";

    /// <summary>
    /// Gas mixer for combining O2 + N2.
    /// </summary>
    [DataField]
    public EntProtoId GasMixer = "GasMixer";

    /// <summary>
    /// Gas port connector.
    /// </summary>
    [DataField]
    public EntProtoId GasPort = "GasPort";

    // --- Misc ---

    /// <summary>
    /// Mini gravity generator.
    /// </summary>
    [DataField]
    public EntProtoId GravityGenerator = "GravityGeneratorMini";

    /// <summary>
    /// Airlock entity for the ship entrance.
    /// </summary>
    [DataField]
    public EntProtoId Airlock = "AirlockGlassShuttle";

    /// <summary>
    /// Number of vent pumps.
    /// </summary>
    [DataField]
    public int VentCount = 2;

    /// <summary>
    /// Number of scrubbers.
    /// </summary>
    [DataField]
    public int ScrubberCount = 2;

    // --- Communications Suite (hard rule) ---

    /// <summary>
    /// Intercom for crew communications.
    /// </summary>
    [DataField]
    public EntProtoId Intercom = "IntercomCommon";

    // --- 2-Stage Airlock (hard rule) ---

    /// <summary>
    /// Interior airlock forming the inner door of the 2-stage airlock.
    /// </summary>
    [DataField]
    public EntProtoId InteriorAirlock = "AirlockGlass";

    // --- Emergency Gear (hard rule) ---

    /// <summary>
    /// Emergency closet with survival supplies.
    /// </summary>
    [DataField]
    public EntProtoId EmergencyCloset = "ClosetEmergency";

    /// <summary>
    /// Fire extinguisher placed on a wall or floor.
    /// </summary>
    [DataField]
    public EntProtoId FireExtinguisher = "FireExtinguisher";

    // --- Utility Systems (hard rule) ---

    /// <summary>
    /// Firelock for compartment sealing during atmosphere breach.
    /// </summary>
    [DataField]
    public EntProtoId Firelock = "Firelock";

    /// <summary>
    /// Air alarm for atmosphere monitoring.
    /// </summary>
    [DataField]
    public EntProtoId AirAlarm = "AirAlarm";

    /// <summary>
    /// Fire alarm for fire detection and response.
    /// </summary>
    [DataField]
    public EntProtoId FireAlarm = "FireAlarm";

    // --- Internal Bulkheads ---

    /// <summary>
    /// Wall entity used for internal bulkheads separating bow/engineering from midship.
    /// </summary>
    [DataField]
    public EntProtoId BulkheadWall = "WallShuttle";

    /// <summary>
    /// Shuttle window entity used on the bow hull to give the cockpit viewports.
    /// </summary>
    [DataField]
    public EntProtoId CockpitWindow = "ShuttleWindow";

    /// <summary>
    /// Lattice tile placed under thrusters so they have a structural anchor in space.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition> LatticeTile = "Lattice";

    /// <summary>
    /// Pipe straight used for the scrubber waste network (separate from the supply network).
    /// </summary>
    [DataField]
    public EntProtoId ScrubberPipe = "GasPipeStraight";

    /// <summary>
    /// Passive vent placed outside the hull on a lattice tile to exhaust the scrubber waste network to space.
    /// </summary>
    [DataField]
    public EntProtoId PassiveVent = "GasPassiveVent";

    /// <summary>
    /// Internal airlock used in bulkhead doorways.
    /// </summary>
    [DataField]
    public EntProtoId BulkheadDoor = "AirlockGlass";

    /// <summary>
    /// Fraction of ship length (from bow end) where the bow bulkhead is placed.
    /// 0.2 means the front 20% is walled off as the cockpit.
    /// </summary>
    [DataField]
    public float BowBulkheadFraction = 0.2f;

    /// <summary>
    /// Fraction of ship length (from stern end) where the engineering bulkhead is placed.
    /// 0.25 means the rear 25% is walled off as engineering.
    /// </summary>
    [DataField]
    public float SternBulkheadFraction = 0.25f;

    /// <summary>
    /// Whether to place internal bulkhead walls separating bow/engineering from midship.
    /// </summary>
    [DataField]
    public bool EnableBulkheads = true;

    /// <summary>
    /// Minimum ship length (in tiles) required to place bulkheads.
    /// Ships shorter than this skip bulkhead placement entirely.
    /// </summary>
    [DataField]
    public int MinBulkheadLength = 6;

    /// <summary>
    /// Minimum tile gap between bow and stern bulkhead lines.
    /// If bulkheads would be closer than this, they are skipped.
    /// </summary>
    [DataField]
    public int MinBulkheadSpacing = 2;

    /// <summary>
    /// Whether to replace bow hull walls with cockpit windows after bulkheads are placed.
    /// </summary>
    [DataField]
    public bool EnableCockpitWindows = true;

    /// <summary>
    /// Width of the bridge room in tiles, centered on the ship centerline.
    /// Bow-section tiles outside this width are filled with walls, creating a narrow,
    /// enclosed bridge room instead of a full-width open section.
    /// 0 = disabled (full-width bow section, legacy behaviour).
    /// </summary>
    [DataField]
    public int BridgeWidth = 3;

    /// <summary>
    /// Whether to place atmosphere equipment (canisters, mixer, gas ports).
    /// When false, only vents and scrubbers are placed (no gas supply source).
    /// </summary>
    [DataField]
    public bool EnableAtmosphere = true;

    /// <summary>
    /// Offset from center X for the scrubber pipe spine column.
    /// 1 means one tile east of center; -1 means one tile west.
    /// </summary>
    [DataField]
    public int ScrubberSpineOffset = 1;

    // --- Decals and Decorations (hard rule) ---

    /// <summary>
    /// Decal placed around the airlock loading area.
    /// </summary>
    [DataField]
    public string AirlockDecal = "BotLeft";

    /// <summary>
    /// Decal used for caution markings near engineering equipment.
    /// </summary>
    [DataField]
    public string CautionDecal = "WarnBox";
}
