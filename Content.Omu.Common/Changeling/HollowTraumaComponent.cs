using Robust.Shared.GameStates;

namespace Content.Omu.Common.Changeling;

/// <summary>
///  Added to linged targets, used to handle hollow revival and tracking
/// </summary>

[RegisterComponent, NetworkedComponent]
public sealed partial class HollowTraumaComponent : Component
{
    [DataField]
    public HollowOrganState OrganState;

    [DataField]
    public int HollowMarkupMessagePriority; // This is set to -1 priority defined in AbsorbedComponent


    public enum HollowOrganState : byte
    {
        Invalid = 0,
        FullyHollow,
        PartiallyRestored,
        FullyRestored
    }
}

