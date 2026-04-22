// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 SolsticeOfTheWinter <solsticeofthewinter@gmail.com>
// SPDX-FileCopyrightText: 2025 TheBorzoiMustConsume <197824988+TheBorzoiMustConsume@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Humanoid;
using Content.Shared.EntityEffects;
using Content.Shared.Humanoid.Prototypes;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Content.Shared.Polymorph.Systems;
using Content.Shared.Polymorph;
using Content.Shared.Polymorph.Components;

namespace Content.Goobstation.Shared.EntityEffects;

[UsedImplicitly]
public sealed partial class SpeciesChange : EventEntityEffect<SpeciesChange>
{
    [DataField(required: true)]
    public ProtoId<SpeciesPrototype> NewSpecies;
    [DataField] public List<ProtoId<SpeciesPrototype>>? AllowedSpecies;

    public SpeciesChange(ProtoId<SpeciesPrototype> newspecies)
    {
        NewSpecies = newspecies;
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-change-species", ("species", NewSpecies));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var _sawmill = Logger.GetSawmill("species-change");

        var humanoidQuery = args.EntityManager.GetEntityQuery<HumanoidAppearanceComponent>();

        if (!humanoidQuery.TryComp(args.TargetEntity, out var targetHumanoid))
        {
            _sawmill.Log(LogLevel.Debug, $"Target has no HumanoidAppearance component?");
            return;
        }

        if (AllowedSpecies != null)
        {
            var targetSpecies = targetHumanoid.Species;

            if (!AllowedSpecies.Contains(targetSpecies))
            {
                return;
            }
        }

        var ev = new SpeciesChange(NewSpecies);
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ev);
    }

}
