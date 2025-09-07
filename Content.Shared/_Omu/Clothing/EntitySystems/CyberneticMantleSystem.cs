using Content.Goobstation.Common.Speech;
using Content.Shared._Omu.Clothing.Components;
using Content.Shared._Omu.Traits;
using Content.Shared.Inventory.Events;
using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Shared._Omu.Clothing.EntitySystems;

public sealed class CyberneticMantleSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CyberneticMantleComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<CyberneticMantleComponent, GotUnequippedEvent>(OnUnequipped);
    }

    /// <summary>
    ///     A list of things to do while the mantle is equpped.
    /// </summary>
    private void OnEquipped(Entity<CyberneticMantleComponent> ent, ref GotEquippedEvent args)
    {
        
    }

    private void OnUnequipped(Entity<CyberneticMantleComponent> ent, ref GotUnequippedEvent args)
    {

    }

}