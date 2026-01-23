using Content.Omu.Shared.Changeling;
using Content.Shared.Humanoid;
using Robust.Shared.GameStates;

namespace Content.Omu.Server.Changeling;


/// <summary>
///  Added to linged targets, used to handle hollow hallucinations
/// </summary>

[RegisterComponent, NetworkedComponent]
public sealed class HollowHallucinationComponent : Component
{
    public List<KillerData> Killers = new();

    [DataField]
    public TimeSpan TimeSinceLastHallucination;
    [DataField]
    public TimeSpan NewHallucinationStartTime;
    [DataField]
    public string LastHallucination = string.Empty;
    [DataField]
    public string NextHallucination = string.Empty;
}

