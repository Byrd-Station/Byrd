using Robust.Shared.GameStates;
namespace Content.Shared._Omu.Components;

/// <summary>
/// Marks a GasTank as exclusive to its own entity's breath mask.
/// When present, this tank will only connect to internals if the entity it is on
/// is also registered as a breath tool (i.e. the mask and tank are the same entity).
/// External tanks in back/suit-storage slots will be ignored in favour of this one.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ExclusiveGasTankComponent : Component;
