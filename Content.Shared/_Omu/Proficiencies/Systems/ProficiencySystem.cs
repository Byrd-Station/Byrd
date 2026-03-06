using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared.GameTicking;
using Content.Shared.Hands;
using Content.Shared.Tools.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Weapons.Ranged.Components;
namespace Content.Shared._Omu.Proficiencies.Systems;

public sealed class ProficiencySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    string? proficiencyID;
    ProficiencyComponent? comp;

    public override void Initialize(){
        SubscribeLocalEvent<ProficiencyComponent, DidEquipHandEvent>(OnEquip);
        SubscribeLocalEvent<ProficiencyComponent, DidUnequipHandEvent>(OnUnequip);
        SubscribeLocalEvent<ProficiencyComponent, PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }


    private void OnEquip(Entity<ProficiencyComponent> entity, ref DidEquipHandEvent args){
        if(comp != null && comp.Items != null)
        {
        bool handled = false;

        if(TryComp<BallisticAmmoProviderComponent>(args.Equipped, out var ammoComp))
        {
            ammoComp.FillDelay *= comp.reloadSpeedProficiency;
        }

        foreach (var Item in comp.Items){
            if(TryComp<MetaDataComponent>(args.Equipped, out var data) && data.EntityPrototype != null)
            {
                if(data.EntityPrototype.ID == Item.Id){
                    handled = true;

                    if(TryComp<ToolComponent>(args.Equipped, out var toolComp)){
                        toolComp.SpeedModifier *= comp.proficiencyMultiplier;

                        Dirty(args.Equipped, toolComp);
                    }

                }
            }
        }
        // If it isnt in the list of items buffed, debuff it instead
        if (!handled)
        {
                if(TryComp<ToolComponent>(args.Equipped, out var toolComp))
                {
                    toolComp.SpeedModifier *= MathF.Pow(comp.proficiencyMultiplier, -1);

                    Dirty(args.Equipped, toolComp);
                }
            }
        }
    }

    private void OnUnequip(Entity<ProficiencyComponent> entity, ref DidUnequipHandEvent args)
    {
        if(comp != null && comp.Items != null)
        {
            if (TryComp<MetaDataComponent>(args.Unequipped, out var data) && data.EntityPrototype != null)
            {
                if (TryComp<ToolComponent>(args.Unequipped, out var toolComp))
                {
                    toolComp.SpeedModifier *= MathF.Pow(toolComp.SpeedModifier, -1);

                    Dirty(args.Unequipped, toolComp);
                }

            }
            if (TryComp<BallisticAmmoProviderComponent>(args.Unequipped, out var ammoComp))
            {
                ammoComp.FillDelay *= MathF.Pow(comp.reloadSpeedProficiency, -1);
            }
        }
    }

    private void OnPlayerSpawnComplete(Entity<ProficiencyComponent> entity, ref PlayerSpawnCompleteEvent args)
    {
        proficiencyID = args.JobId;

        comp = null;

        if (proficiencyID == null)
        {
            return;
        }

        if (proficiencyID != null && args.JobId != null && _prototypeManager.TryIndex<ProficiencyPrototype>(args.JobId, out var proficiencyPrototype))
        {
            entity.Comp.Items = proficiencyPrototype.Items;
            entity.Comp.proficiencyMultiplier = proficiencyPrototype.proficiencyMultiplier;
            entity.Comp.surgeryProficiency = proficiencyPrototype.surgeryProficiency;
            entity.Comp.reloadSpeedProficiency = proficiencyPrototype.reloadSpeedProficiency;

            comp = entity.Comp;

            if (comp.surgeryProficiency != 1f)
            {
                var surgerySpeedComp = _entityManager.EnsureComponent<SurgerySpeedModifierComponent>(args.Mob);
                surgerySpeedComp.SpeedModifier *= comp.surgeryProficiency;

                Dirty(args.Mob, surgerySpeedComp);
            }
        }
    }
}
