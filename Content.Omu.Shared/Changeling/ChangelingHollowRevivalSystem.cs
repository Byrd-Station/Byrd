using Content.Omu.Common.Changeling;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Body.Events;
using Content.Shared.Examine;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;

namespace Content.Omu.Shared.Changeling;

public sealed class SharedChangelingHollowRevivalSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HollowTraumaComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(Entity<HollowTraumaComponent> ent, ref ExaminedEvent args)
    {
        if (_mobState.IsAlive(ent))
            return;

        var text = ent.Comp.OrganState switch
        {

            HollowTraumaComponent.HollowOrganState.FullyHollow =>
                Loc.GetString("changeling-hollowed-onexamine-fullhollow"),

            HollowTraumaComponent.HollowOrganState.PartiallyRestored =>
                Loc.GetString("changeling-hollowed-onexamine-partialhollow"),

            HollowTraumaComponent.HollowOrganState.FullyRestored =>
                Loc.GetString("changeling-hollowed-onexamine-filled"),

            _ => null
        };

        if (text == null)
            return;

        args.PushMarkup(text, ent.Comp.HollowMarkupMessagePriority);
    }

}
