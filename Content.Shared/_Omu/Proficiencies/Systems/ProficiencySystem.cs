using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared.GameTicking;
using Content.Shared.Hands;
using Content.Shared.Tools.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Weapons.Ranged.Components;
namespace Content.Shared._Omu.Proficiencies;

public sealed class ProficiencySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    string? proficiencyID;
    ProficiencyPrototype? proto;

    public override void Initialize(){
        SubscribeLocalEvent<ProficiencyComponent, DidEquipHandEvent>(OnEquip);
        SubscribeLocalEvent<ProficiencyComponent, DidUnequipHandEvent>(OnUnequip);
        SubscribeLocalEvent<ProficiencyComponent, PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }


    private void OnEquip(Entity<ProficiencyComponent> entity, ref DidEquipHandEvent args){
        if(proto != null && proto.Items != null)
        {
        bool handled = false;

        if(TryComp<BallisticAmmoProviderComponent>(args.Equipped, out var ammoComp))
        {
            ammoComp.FillDelay *= proto.reloadSpeedProficiency;
        }

        foreach (var Item in proto.Items){
            if(TryComp<MetaDataComponent>(args.Equipped, out var data) && data.EntityPrototype != null)
            {
                if(data.EntityPrototype.ID == Item.Id){
                    handled = true;

                    if(TryComp<ToolComponent>(args.Equipped, out var toolComp)){
                        toolComp.SpeedModifier *= proto.proficiencyMultiplier;

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
                    toolComp.SpeedModifier *= MathF.Pow(proto.proficiencyMultiplier, -1);

                    Dirty(args.Equipped, toolComp);
                }
            }
        }
    }

    private void OnUnequip(Entity<ProficiencyComponent> entity, ref DidUnequipHandEvent args)
    {
        if(proto != null && proto.Items != null)
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
                ammoComp.FillDelay *= MathF.Pow(proto.reloadSpeedProficiency, -1);
            }
        }
    }

    private void OnPlayerSpawnComplete(Entity<ProficiencyComponent> entity, ref PlayerSpawnCompleteEvent args)
    {
        proficiencyID = args.JobId;

        proto = null;

        if (proficiencyID == null)
        {
            return;
        }

        if (proficiencyID != null && args.JobId != null && _prototypeManager.TryIndex<ProficiencyPrototype>(args.JobId, out var proficiencyPrototype))
        {
            proto = proficiencyPrototype;

            if (proto.surgeryProficiency != 1f)
            {
                var surgerySpeedComp = _entityManager.EnsureComponent<SurgerySpeedModifierComponent>(args.Mob);
                surgerySpeedComp.SpeedModifier *= proto.surgeryProficiency;

                Dirty(args.Mob, surgerySpeedComp);
            }
        }
    }
}
