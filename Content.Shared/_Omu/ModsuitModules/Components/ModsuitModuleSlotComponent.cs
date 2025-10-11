using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Omu.ModsuitModules.Components;

[RegisterComponent]
public sealed partial class ModsuitModuleSlotComponent : Component
{
    /// <summary>
    /// The actual item-slot that contains the module. Allows all the interaction logic to be handled by <see cref="ItemSlotsSystem"/>.
    /// </summary>

    [DataField("ModuleSlotId", required: true)]
    public string ModuleSlotId = string.Empty;

}

/// <summary>
///     Raised directed at an entity with a module slot when the module inside is ejected/inserted.
/// </summary>
public sealed class ModsuitModuleChangedEvent : EntityEventArgs
{
    public bool Ejected;

    public void ModsuitModulesChangedEvent(bool ejected)
    {
        Ejected = ejected;
    }
}
