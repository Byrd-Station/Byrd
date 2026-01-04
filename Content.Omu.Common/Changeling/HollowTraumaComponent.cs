using Robust.Shared.GameStates;

namespace Content.Omu.Common.Changeling;

[RegisterComponent, NetworkedComponent]
public sealed partial class HollowTraumaComponent : Component
{
    [DataField]
    public EntityUid TraumaInflicter;
    [DataField]
    public TimeSpan TimeSinceLastHallucination;
    [DataField]
    public TimeSpan NewHallucinationStartTime;
    [DataField]
    public string LastHallucination = string.Empty;
    [DataField]
    public string NextHallucination = string.Empty;
    [DataField]
    public HollowOrganState OrganState;

    [DataField]
    public const int HollowMarkupMessagePriority = -10; // As low as we can


    public enum HollowOrganState : byte
    {
        Invalid = 0,          // safety / default
        MissingBrain,         // no brain present
        FullyHollow,          // no vital organs at all
        PartiallyRestored,    // some organs present
        FullyRestored         // all required organs present
    }

}

