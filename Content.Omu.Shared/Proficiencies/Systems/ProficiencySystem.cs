using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared.GameTicking;
using Content.Shared.Hands;
using Content.Shared.Tools.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Weapons.Ranged.Components;
using Content.Omu.Common.Proficiencies;

namespace Content.Omu.Shared.Proficiencies.Systems;

public sealed class ProficiencySystem : CommonProficiencySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;


    public override void Initialize(){
        SubscribeLocalEvent<ProficiencyComponent, DidEquipHandEvent>(OnEquip);
        SubscribeLocalEvent<ProficiencyComponent, DidUnequipHandEvent>(OnUnequip);
        SubscribeLocalEvent<ProficiencyComponent, PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }


    private void OnEquip(Entity<ProficiencyComponent> entity, ref DidEquipHandEvent args){
        if(entity.Comp.Items != null)
        {
        bool handled = false;

        if(TryComp<BallisticAmmoProviderComponent>(args.Equipped, out var ammoComp))
        {
            ammoComp.FillDelay *= entity.Comp.ReloadSpeedProficiency;
        }

        foreach (var Item in entity.Comp.Items){
            if(TryComp<MetaDataComponent>(args.Equipped, out var data) && data.EntityPrototype != null)
            {
                if(data.EntityPrototype.ID == Item.Id){
                    handled = true;

                    if(TryComp<ToolComponent>(args.Equipped, out var toolComp)){
                        toolComp.SpeedModifier *= entity.Comp.ProficiencyMultiplier;

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
                    toolComp.SpeedModifier *= MathF.Pow(entity.Comp.ProficiencyMultiplier, -1);

                    Dirty(args.Equipped, toolComp);
                }
            }
        }
    }

    private void OnUnequip(Entity<ProficiencyComponent> entity, ref DidUnequipHandEvent args)
    {
        if(entity.Comp != null && entity.Comp.Items != null)
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
                ammoComp.FillDelay *= MathF.Pow(entity.Comp.ReloadSpeedProficiency, -1);
            }
        }
    }

    private void OnPlayerSpawnComplete(Entity<ProficiencyComponent> entity, ref PlayerSpawnCompleteEvent args)
    {
        entity.Comp.ProficiencyID = args.JobId;

        if (entity.Comp.ProficiencyID == null)
        {
            return;
        }

        if (entity.Comp.ProficiencyID != null && args.JobId != null && _prototypeManager.TryIndex<ProficiencyPrototype>(args.JobId, out var proficiencyPrototype))
        {
            entity.Comp.Items = proficiencyPrototype.Items;
            entity.Comp.ProficiencyMultiplier = proficiencyPrototype.proficiencyMultiplier;
            entity.Comp.SurgeryProficiency = proficiencyPrototype.surgeryProficiency;
            entity.Comp.ReloadSpeedProficiency = proficiencyPrototype.reloadSpeedProficiency;

            if (entity.Comp.SurgeryProficiency != 1f)
            {
                var surgerySpeedComp = _entityManager.EnsureComponent<SurgerySpeedModifierComponent>(args.Mob);
                surgerySpeedComp.SpeedModifier *= entity.Comp.SurgeryProficiency;

                Dirty(args.Mob, surgerySpeedComp);
            }
        }
    }
}
