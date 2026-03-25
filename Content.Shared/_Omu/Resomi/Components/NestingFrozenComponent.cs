using Robust.Shared.GameStates;

namespace Content.Shared._Omu.Resomi.Components;

/// <summary>
/// Added to a Resomi when it enters nesting state.
/// Prevents movement and most actions, but allows speech, emotes, and the exit-nest action.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NestingFrozenComponent : Component
{
}
