// SPDX-FileCopyrightText: 2026 Goob Station Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.EntityEffects;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;
using Content.Shared.EntityEffects;

using Content.Shared.Humanoid;
using Robust.Shared.GameObjects;

namespace Content.Goobstation.Shared.EntityEffects;
public sealed partial class RandomSpeciesChange : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-change-species-random");

    [DataField] public List<ProtoId<SpeciesPrototype>>? AllowedSpecies;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var protMan = IoCManager.Resolve<IPrototypeManager>();
        var random = IoCManager.Resolve<IRobustRandom>();
        var entityEffects = args.EntityManager.System<SharedEntityEffectSystem>();

        // whatever, go my rngesus
        var species = protMan.EnumeratePrototypes<SpeciesPrototype>();

        if (AllowedSpecies != null && AllowedSpecies.Count > 0)
            species = species.Where(q => AllowedSpecies.Any(w => q.ID == w));

        if (args.TargetEntity == null)
            return;

        var humanoidQuery = args.EntityManager.GetEntityQuery<HumanoidAppearanceComponent>();

        if (!humanoidQuery.TryComp(args.TargetEntity, out var targetHumanoid))
            return;

        if (AllowedSpecies != null)
        {
            var targetSpecies = targetHumanoid.Species;

            if (!AllowedSpecies.Contains(targetSpecies))
            {
                return;
            }
        }

        var sce = new SpeciesChange
        {
            NewSpecies = random.Pick(species.ToList()).ID,
        };

        entityEffects.Effect(sce, args);
    }
}
