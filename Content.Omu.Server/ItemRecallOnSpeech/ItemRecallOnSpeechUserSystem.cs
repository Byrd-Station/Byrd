using System.Text.RegularExpressions;
using Content.Server.Chat.Systems;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Omu.Server.ItemRecallOnSpeech;

public sealed class ItemRecallOnSpeechUserSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = null!;
    [Dependency] private readonly SharedContainerSystem _container = null!;
    [Dependency] private readonly SharedHandsSystem _hands = null!;
    [Dependency] private readonly SharedAudioSystem _audio = null!;
    [Dependency] private readonly SharedPopupSystem _popup = null!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UseDelaySystem _delaySystem = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemRecallOnSpeechUserComponent, EntitySpokeEvent>(OnEntitySpoke);
    }

    private void OnEntitySpoke(Entity<ItemRecallOnSpeechUserComponent> user, ref EntitySpokeEvent args)
    {
        if (user.Comp.ItemsToRecall.Count == 0)
            return;

        var itemsPendingRecall = new List<(Entity<ItemRecallOnSpeechComponent> Pending, EntityUid Target)>();

        foreach (var recallable in user.Comp.ItemsToRecall)
        {
            if (recallable.Comp.EntityToRecallTo is not { } entityToRecallTo
                || recallable.Comp.RecallPhrase is not { } recallPhrase
                || !DoesMessageContainPhrase(args.Message, recallPhrase))
                continue;

            // check cooldown
            if (_delaySystem.IsDelayed(recallable.Owner, recallable.Comp.UseDelayId)
                && _delaySystem.TryGetDelayInfo(recallable.Owner, out var useDelayInfo, recallable.Comp.UseDelayId))
            {
                var remainingTime = Math.Round((useDelayInfo.EndTime - _timing.CurTime).TotalSeconds);
                _popup.PopupEntity(Loc.GetString(recallable.Comp.FailCooldownPopup, ("item", Name(recallable.Owner)), ("timeRemaining", remainingTime)), user, user);
                continue;
            }

            // check if someone's holding it
            if (recallable.Comp.PreventRecallIfHeld
                && _container.TryGetContainingContainer(recallable.Owner, out var container)
                && HasComp<MindContainerComponent>(container.Owner))
            {
                _popup.PopupEntity(Loc.GetString(recallable.Comp.FailHeldPopup, ("item", Name(recallable.Owner))), user, user);
                continue;
            }


            itemsPendingRecall.Add((recallable, entityToRecallTo));
        }

        if (itemsPendingRecall.Count == 0)
            return;

        foreach (var (pending, target) in itemsPendingRecall)
        {
            // To the hands!
            _transform.SetCoordinates(pending, Transform(target).Coordinates);
            _hands.TryPickupAnyHand(target, pending);

            // Cooldown!
            if (TryComp<UseDelayComponent>(pending, out var useDelay))
                _delaySystem.TryResetDelay((pending, useDelay), true, pending.Comp.UseDelayId);

            // Pizazz!
            Spawn(pending.Comp.EffectProto, Transform(target).Coordinates);
            _audio.PlayPvs(pending.Comp.SoundPath, target, AudioParams.Default.WithVolume(-4f));

            // If damage is set, then damage!
            // This is purposefully configurable so you can make weird admin shit with it. Go ham.
            if (pending.Comp is { DoDamageOnRecall: true, DamageOnRecall: { } damageOnRecall })
                _damageable.TryChangeDamage(target, damageOnRecall, true, targetPart: pending.Comp.PartToDamage);
        }
    }

    private bool DoesMessageContainPhrase(string message, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            return false;

        var escapedPhrase = Regex.Escape(phrase);
        var pattern =  $@"(^|\W){escapedPhrase}($|\W)";

        return Regex.IsMatch(message, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }


}
