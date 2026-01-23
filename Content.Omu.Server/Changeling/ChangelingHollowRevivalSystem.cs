using Content.Goobstation.Common.Changeling;
using Content.Omu.Common.Changeling;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Examine;
using Content.Shared.Forensics.Components;
using Content.Shared.Humanoid;
using Content.Shared.Traits.Assorted;
using Robust.Server.Containers;

namespace Content.Omu.Server.Changeling;

public sealed class ChangelingHollowRevivalSystem : OmuChangelingCommunicator
{
    [Dependency] private readonly BodySystem _bodySystem = null!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HollowTraumaComponent, ComponentStartup>(OnStartup);
        // Organ-side events
        SubscribeLocalEvent<OrganComponent, OrganAddedEvent>(OnOrganAdded);
        SubscribeLocalEvent<OrganComponent, OrganRemovedEvent>(OnOrganRemoved);
    }

    private void OnStartup(Entity<HollowTraumaComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<AbsorbedComponent>(ent.Owner, out var absComp))
            return;

        ent.Comp.HollowMarkupMessagePriority = absComp.examinePriority - 1;
        UpdateOrganState(ent.Owner, ent.Comp);
    }

    private void OnOrganAdded(EntityUid organUid, OrganComponent organ, ref OrganAddedEvent args)
    {
        TryUpdateOwningBodyState(args.Part);
    }

    private void OnOrganRemoved(EntityUid organUid, OrganComponent organ, ref OrganRemovedEvent args)
    {
        TryUpdateOwningBodyState(args.OldPart); // pass bodypart instead of organ cause yknow we remove the organ
    }

    /// <summary>
    ///     Takes either a bodypart uid
    ///     If an organ is added or removed pass the bodypart uid from which it was added/removed from.
    /// </summary>
    private void TryUpdateOwningBodyState(EntityUid uid)
    {
        if (!TryComp<BodyPartComponent>(uid, out var bodyPart))
            return;
        if (!TryComp<HollowTraumaComponent>(bodyPart.Body!.Value, out var hollow))
            return;
        UpdateOrganState(bodyPart.Body.Value, hollow);
    }


    private void UpdateOrganState(EntityUid bodyUid, HollowTraumaComponent comp)
    {
        var oldState = comp.OrganState;
        var newState = EvaluateOrganState(bodyUid);

        if (oldState == newState)
            return;

        comp.OrganState = newState;
        Dirty(bodyUid, comp);

        OnOrganStateChanged(bodyUid, comp, oldState, newState);
    }

    private void OnOrganStateChanged(
        EntityUid uid,
        HollowTraumaComponent comp,
        HollowTraumaComponent.HollowOrganState oldState,
        HollowTraumaComponent.HollowOrganState newState)
    {
        if (newState == HollowTraumaComponent.HollowOrganState.FullyRestored && HasComp<UnrevivableComponent>(uid))
        {
            EntityManager.RemoveComponent<UnrevivableComponent>(uid);
            return;
        }
        else
        {
            EntityManager.EnsureComponent<UnrevivableComponent>(uid, out var unrevComp);
            unrevComp.Cloneable = false;
            return;
        }
    }

    private HollowTraumaComponent.HollowOrganState EvaluateOrganState(EntityUid uid)
    {
        var organCount =
            (_bodySystem.TryGetBodyOrganEntityComps<HeartComponent>(uid, out _) ? 1 : 0) +
            (_bodySystem.TryGetBodyOrganEntityComps<LungComponent>(uid, out _) ? 1 : 0) +
            (_bodySystem.TryGetBodyOrganEntityComps<StomachComponent>(uid, out _) ? 1 : 0) +
            (_bodySystem.TryGetBodyOrganEntityComps<LiverComponent>(uid, out _) ? 1 : 0);

        return organCount switch
        {
            0 => HollowTraumaComponent.HollowOrganState.FullyHollow,
            < 4 => HollowTraumaComponent.HollowOrganState.PartiallyRestored,
            _ => HollowTraumaComponent.HollowOrganState.FullyRestored
        };
    }


    public override void RemoveOrgansOnAbsorb(EntityUid target)
    {
        if (!TryComp<BodyComponent>(target, out var body))
            return;
        var parts = _bodySystem.GetBodyChildren(target, body);
        foreach (var part in parts)
        {
            foreach (var (organ, _) in _bodySystem.GetPartOrgans(part.Id, part.Component))
            {
                if (HasComp<BrainComponent>(organ))
                    continue;
                _bodySystem.RemoveOrgan(organ);
                var removedEv = new OrganRemovedEvent(organ, part.Id);
                RaiseLocalEvent(organ, ref removedEv);
                EntityManager.QueueDeleteEntity(organ);
            }
        }
    }

    public override void SetupLingData(Entity<Component> ling, EntityUid lingMind, EntityUid target) // comp here is ChangelingIdentityComp
    {
        var htComp = EnsureComp<HollowTraumaComponent>(target);
        var hHalComp = EnsureComp<HollowHallucinationComponent>(target);

        if (!TryComp<DnaComponent>(ling, out var dnaComp) ||
            !TryComp<HumanoidAppearanceComponent>(ling, out var hApComp))
        {
            return;
        }

        var kData =
            new KillerData(
                ling,
                lingMind,
                dnaComp.DNA,
                (ling.Owner , hApComp),
                null
                );

        hHalComp.Killers.Add(kData);

        var ev = new HollowKillerAddedEvent() { Data = kData };
        RaiseNetworkEvent(ev, target);

    }
}
