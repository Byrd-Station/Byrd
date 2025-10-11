using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Omu.ModsuitModules.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModsuitModuleComponent : Component
{
    /// <summary>
    /// Relevant suit this module is attached to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Modsuit;

    /// <summary>
    ///     When attached, the module will ensure these components on the entity, and delete them on removal.
    /// </summary>
    [DataField]
    public ComponentRegistry? OnAdd;

    /// <summary>
    ///     When removed, the module will ensure these components on the entity, and delete them on insertion.
    /// </summary>
    [DataField]
    public ComponentRegistry? OnRemove;

    /// <summary>
    ///     Is this module working or not?
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    ///     Can this module be enabled or disabled? Used mostly for prop, damaged or useless modules.
    /// </summary>
    [DataField]
    public bool CanEnable = true;
}
