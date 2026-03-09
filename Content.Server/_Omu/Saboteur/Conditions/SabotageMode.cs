namespace Content.Server._Omu.Saboteur.Conditions;

/// <summary>
/// Defines how a map entity is considered "sabotaged" for threshold-based objectives.
/// </summary>
public enum SabotageMode
{
    /// <summary>
    /// The entity's <c>ApcPowerReceiverComponent</c> reports no power.
    /// </summary>
    Unpowered,

    /// <summary>
    /// A surveillance camera entity is marked inactive.
    /// </summary>
    CameraInactive,

    /// <summary>
    /// An APC whose main breaker is off or that receives no external power.
    /// </summary>
    ApcDisabled,

    /// <summary>
    /// An <c>EncryptionKeyHolderComponent</c> whose key container is empty
    /// (e.g. a telecomms machine stripped of its encryption keys).
    /// </summary>
    KeysEmpty,

    /// <summary>
    /// The target entity has been destroyed (deleted from the world).
    /// </summary>
    Destroyed,

    /// <summary>
    /// A door whose bolts have been engaged (bolted shut).
    /// </summary>
    DoorBolted,
}
