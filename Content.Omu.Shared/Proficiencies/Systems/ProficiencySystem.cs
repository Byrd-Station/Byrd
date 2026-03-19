using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared.GameTicking;
using Content.Shared.Hands;
using Content.Shared.Tools.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Weapons.Ranged.Components;
using Content.Omu.Common.Proficiencies;
using Content.Omu.Shared.Proficiencies.Components;

namespace Content.Omu.Shared.Proficiencies.Systems;
public sealed class ProficiencySystem : CommonProficiencySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    
    public override void Initialize()
    {
        SubscribeLocalEvent<ProficiencyComponent, DidEquipHandEvent>(OnEquip);
        SubscribeLocalEvent<ProficiencyComponent, DidUnequipHandEvent>(OnUnequip);
        SubscribeLocalEvent<ProficiencyComponent, PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }


    private void OnEquip(Entity<ProficiencyComponent> entity, ref DidEquipHandEvent args)
    {
        if (entity.Comp.Items == null
            || entity.Comp.Items.Count.Equals(0))
            return;

        var handled = false;

        if(TryComp<BallisticAmmoProviderComponent>(args.Equipped, out var ammoComp))
            ammoComp.FillDelay *= entity.Comp.ReloadSpeedProficiency;

        if (!TryComp<ToolComponent>(args.Equipped, out var toolComp))
            return;

        foreach (var item in entity.Comp.Items)
        {
            var data = MetaData(args.Equipped);

            if (data.EntityPrototype == null || data.EntityPrototype.ID != item.Id)
                continue;

            toolComp.SpeedModifier *= entity.Comp.ProficiencyMultiplier;
            Dirty(args.Equipped, toolComp);

            handled = true;
        }
        // If it isnt in the list of items buffed, debuff it instead
        if (handled)
            return;

        toolComp.SpeedModifier *= MathF.Pow(entity.Comp.ProficiencyMultiplier, -1);
        Dirty(args.Equipped, toolComp);
    }

    private void OnUnequip(Entity<ProficiencyComponent> entity, ref DidUnequipHandEvent args)
    {
        if (entity.Comp.Items == null
            || entity.Comp.Items.Count.Equals(0))
            return;

        var meta = MetaData(args.Unequipped);
        if (meta.EntityPrototype == null)
            return;

        if (TryComp<ToolComponent>(args.Unequipped, out var toolComp))
        {
            toolComp.SpeedModifier *= MathF.Pow(toolComp.SpeedModifier, -1);
            Dirty(args.Unequipped, toolComp);
        }

        if (TryComp<BallisticAmmoProviderComponent>(args.Unequipped, out var ammoComp))
            ammoComp.FillDelay *= MathF.Pow(entity.Comp.ReloadSpeedProficiency, -1);

    }

    private void OnPlayerSpawnComplete(Entity<ProficiencyComponent> entity, ref PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null)
            return;

        entity.Comp.ProficiencyID = args.JobId;

        if (!_prototypeManager.TryIndex<ProficiencyPrototype>(entity.Comp.ProficiencyID, out var proficiencyPrototype))
            return;

        entity.Comp.Items = proficiencyPrototype.Items;
        entity.Comp.ProficiencyMultiplier = proficiencyPrototype.ProficiencyMultiplier;
        entity.Comp.SurgeryProficiency = proficiencyPrototype.SurgeryProficiency;
        entity.Comp.ReloadSpeedProficiency = proficiencyPrototype.ReloadSpeedProficiency;
        if (entity.Comp.SurgeryProficiency == 1f)
            return;

        var surgerySpeedComp = EnsureComp<SurgerySpeedModifierComponent>(args.Mob);
        surgerySpeedComp.SpeedModifier *= entity.Comp.SurgeryProficiency;

        Dirty(args.Mob, surgerySpeedComp);
    }
}
