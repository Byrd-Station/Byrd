using Content.Shared.Humanoid;
using Robust.Shared.Serialization;

namespace Content.Omu.Shared.Changeling;

[Serializable, NetSerializable]
public sealed class HollowKillerAddedEvent : EntityEventArgs
{
    public KillerData Data = new();
}
public record struct KillerData(
    EntityUid TraumaInflicterBody,
    EntityUid TraumaInflicterMind,
    string? KillerDna,
    Entity<HumanoidAppearanceComponent> KillerHumanoidAppearance,
    EntityUid? KillerWeaponUid
);
