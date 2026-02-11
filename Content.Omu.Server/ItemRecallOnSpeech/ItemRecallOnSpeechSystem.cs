using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Omu.Server.ItemRecallOnSpeech;

public sealed class ItemRecallOnSpeechSystem : EntitySystem
{
    [Dependency] private readonly UseDelaySystem _delaySystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = null!;
    [Dependency] private readonly SharedPopupSystem _popup = null!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemRecallOnSpeechComponent, ComponentShutdown>(OnComponentShutdown);

        SubscribeLocalEvent<ItemRecallOnSpeechComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnComponentShutdown(Entity<ItemRecallOnSpeechComponent> recallable, ref ComponentShutdown args)
    {
        // Otherwise shit goes boom lmfao.
        if (recallable.Comp.EntityToRecallTo is {} entityToRecallTo)
            RemComp<ItemRecallOnSpeechUserComponent>(entityToRecallTo);
    }

    private void OnGetVerbs(Entity<ItemRecallOnSpeechComponent> recallable, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract
            || !args.CanComplexInteract
            || recallable.Comp.EntityToRecallTo != null)
            return;

        var user = args.User;
        var itemToRecall = args.Target;

        AlternativeVerb bindItemVerb = new()
        {
            Act = () =>
            {
                SetActivationPhrase(recallable, Name(itemToRecall));
                SetEntityToRecallTo((itemToRecall, recallable), user);
            },
            Text = Loc.GetString("item-recall-component-bind-item-verb"),
            Message = Loc.GetString("item-recall-component-bind-item-verb-text"),
            Priority = 2,
        };

        args.Verbs.Add(bindItemVerb);
    }

    private void SetEntityToRecallTo(Entity<ItemRecallOnSpeechComponent> recallable, EntityUid entity)
    {
        if (recallable.Comp.EntityToRecallTo != null)
            return;

        _delaySystem.SetLength(recallable.Owner, recallable.Comp.RecallCooldown, recallable.Comp.UseDelayId);

        recallable.Comp.EntityToRecallTo = entity;

        var userComponent = EnsureComp<ItemRecallOnSpeechUserComponent>(entity);
        userComponent.ItemsToRecall.Add(recallable);

        // vfx
        Spawn(recallable.Comp.EffectProto, Transform(entity).Coordinates);
        _audio.PlayPvs(recallable.Comp.SoundPath, entity, AudioParams.Default.WithVolume(-4f));

        _popup.PopupEntity(Loc.GetString("item-recall-component-item-bound-popup", ("item", Name(recallable.Owner))), entity, entity);
    }

    private static void SetActivationPhrase(ItemRecallOnSpeechComponent recallable, string newPhrase)
    {
        if (recallable.EntityToRecallTo == null)
            recallable.RecallPhrase = newPhrase;
    }
}
