using Robust.Shared.Prototypes;

namespace Content.Server._Omu.Nutrition.Components;

/// <summary>
/// When attached to a smokable entity, periodically spawns tesla energy balls
/// at the smoker's location.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class SupermatterSmokeComponent : Component
{
    /// <summary>
    /// The entity prototype to spawn periodically while the smoke is active.
    /// </summary>
    [DataField]
    public EntProtoId TeslaPrototype = "TeslaMiniEnergyBall";

    /// <summary>
    /// Minimum time between tesla ball spawns.
    /// </summary>
    [DataField]
    public TimeSpan MinSpawnInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum time between tesla ball spawns.
    /// </summary>
    [DataField]
    public TimeSpan MaxSpawnInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The next game time at which a tesla ball will be spawned.
    /// </summary>
    [AutoPausedField]
    public TimeSpan NextSpawnTime;

    /// <summary>
    /// Whether the smoke is currently lit and being actively smoked.
    /// </summary>
    public bool IsActive;
}
