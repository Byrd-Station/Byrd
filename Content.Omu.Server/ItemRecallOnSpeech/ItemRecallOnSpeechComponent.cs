using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Omu.Server.ItemRecallOnSpeech;

[RegisterComponent]
public sealed partial class ItemRecallOnSpeechComponent : Component
{
    /// <summary>
    /// The phrase used to recall the item.
    /// </summary>
    [DataField]
    public string? RecallPhrase;

    /// <summary>
    /// The entity to teleport the item back to when triggered.
    /// </summary>
    [DataField]
    public EntityUid? EntityToRecallTo;

    /// <summary>
    /// How long between recalls.
    /// </summary>
    [DataField]
    public TimeSpan RecallCooldown = TimeSpan.FromSeconds(60f);

    /// <summary>
    /// Should this item damage the user when recalled?
    /// </summary>
    [DataField]
    public bool DoDamageOnRecall = true;

    /// <summary>
    /// Which part to damage when recalled?
    /// </summary>
    [DataField]
    public TargetBodyPart PartToDamage = TargetBodyPart.Hands;

    /// <summary>
    /// How much damage is dealt when recalled.
    /// </summary>
    [DataField]
    public DamageSpecifier DamageOnRecall = null!;

    /// <summary>
    /// The ID for the use delay.
    /// </summary>
    [DataField]
    public string UseDelayId = "ItemRecallDelay";

    [DataField]
    public LocId FailCooldownPopup = "item-recall-component-fail-cooldown-popup";

    [DataField]
    public LocId FailHeldPopup = "item-recall-component-fail-held-popup";

    /// <summary>
    /// If someone else is holding this item, prevent recall.
    /// </summary>
    [DataField]
    public bool PreventRecallIfHeld = true;

    /// <summary>
    /// Which effect to display.
    /// </summary>
    [DataField]
    public EntProtoId EffectProto = "EffectSpark";

    /// <summary>
    /// Which sound effect to play.
    /// </summary>
    [DataField]
    public SoundSpecifier? SoundPath;
}
