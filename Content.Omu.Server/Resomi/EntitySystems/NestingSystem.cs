// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Body.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Content.Omu.Shared.Resomi.Components;
using Content.Omu.Shared.Resomi.EntitySystems;
using Content.Omu.Shared.Resomi.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Omu.Server.Resomi.EntitySystems;

public sealed class NestingSystem : SharedNestingSystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NestingComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NestingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NestingComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<NestingComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<NestingComponent, EnterNestActionEvent>(OnEnterNest);
        SubscribeLocalEvent<NestingComponent, ExitNestActionEvent>(OnExitNest);
        SubscribeLocalEvent<NestingComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<NestingComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // Expire pending nest requests
            if (comp.RequestingNest && curTime > comp.NestRequestExpiry)
            {
                comp.RequestingNest = false;
                Dirty(uid, comp);
                _popup.PopupEntity(Loc.GetString("nesting-request-expired"), uid, uid, PopupType.SmallCaution);
            }

            if (!comp.IsNesting || comp.NextHeal > curTime)
                continue;

            // Stop healing if entity is somehow moving (left the nest position)
            if (!HasComp<NestingFrozenComponent>(uid))
            {
                comp.IsNesting = false;
                Dirty(uid, comp);
                continue;
            }

            var modifier = _mobState.IsCritical(uid) ? -comp.CritHealingModifier : -1.0f;
            _damageable.TryChangeDamage(uid, modifier * comp.HealingPerUpdate, true, origin: uid);
            _bloodstream.TryModifyBleedAmount(uid, modifier * comp.BleedHealPerUpdate);
            comp.NextHeal += comp.UpdateInterval;
        }
    }

    private void OnMapInit(EntityUid uid, NestingComponent comp, MapInitEvent args)
    {
        _actions.AddAction(uid, ref comp.EnterNestActionEntity, comp.EnterNestAction);
    }

    private void OnShutdown(EntityUid uid, NestingComponent comp, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, comp.EnterNestActionEntity);
        _actions.RemoveAction(uid, comp.ExitNestActionEntity);

        if (comp.ContinuousEffectEntity.HasValue)
            QueueDel(comp.ContinuousEffectEntity.Value);
    }

    private void OnMobStateChanged(Entity<NestingComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && ent.Comp.IsNesting)
            RaiseLocalEvent(ent.Owner, new ExitNestActionEvent());
    }

    /// <summary>
    /// Reduces all incoming positive damage by NestDamageReduction while nesting (pain tolerance).
    /// </summary>
    private void OnDamageModify(Entity<NestingComponent> ent, ref DamageModifyEvent args)
    {
        if (!ent.Comp.IsNesting || args.Origin == ent.Owner)
            return;

        var updated = new DamageSpecifier();
        foreach (var (type, value) in args.Damage.DamageDict)
        {
            updated.DamageDict[type] = value > 0 ? ent.Comp.NestDamageReduction * value : value;
        }
        args.Damage = updated;
    }

    /// <summary>
    /// Returns nearby alive player-controlled Resomis (excluding self).
    /// </summary>
    private List<EntityUid> GetNearbyResomis(EntityUid uid, NestingComponent comp)
    {
        var result = new List<EntityUid>();
        var xform = Transform(uid);
        var nearby = _lookup.GetEntitiesInRange(xform.Coordinates, comp.NestPartnerRange);
        foreach (var ent in nearby)
        {
            if (ent == uid)
                continue;
            if (!HasComp<NestingComponent>(ent))
                continue;
            if (!TryComp<ActorComponent>(ent, out _))
                continue;
            if (TryComp<MobStateComponent>(ent, out var mobState) && _mobState.IsAlive(ent, mobState))
                result.Add(ent);
        }
        return result;
    }

    /// <summary>
    /// Adds Allow/Deny nesting verbs when right-clicking on a Resomi that is requesting to nest.
    /// </summary>
    private void OnGetVerbs(EntityUid uid, NestingComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        // uid = the Resomi being right-clicked (requesting nest)
        // args.User = the player doing the right-click
        if (!comp.RequestingNest)
            return;
        if (args.User == uid)
            return;
        // The responding player must also be a Resomi
        if (!HasComp<NestingComponent>(args.User))
            return;
        if (!args.CanAccess || !args.CanInteract)
            return;

        var requesterName = _entMan.GetComponent<MetaDataComponent>(uid).EntityName;

        var allowVerb = new InteractionVerb
        {
            Text = Loc.GetString("nesting-verb-allow", ("name", (object) requesterName)),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/in.svg.192dpi.png")),
            Act = () => AllowNesting(uid, comp, args.User)
        };
        var denyVerb = new InteractionVerb
        {
            Text = Loc.GetString("nesting-verb-deny", ("name", (object) requesterName)),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/out.svg.192dpi.png")),
            Act = () => DenyNesting(uid, comp, args.User)
        };

        args.Verbs.Add(allowVerb);
        args.Verbs.Add(denyVerb);
    }

    private void AllowNesting(EntityUid uid, NestingComponent comp, EntityUid approver)
    {
        if (!comp.RequestingNest)
            return;
        comp.RequestingNest = false;

        // Enter the nest for the requester (Resomi A)
        comp.NestPartner = approver;
        DoEnterNest(uid, comp);
        _popup.PopupEntity(Loc.GetString("nesting-partner-accepted"), uid, uid, PopupType.Medium);

        // Also enter the nest for the approver (Resomi B)
        if (TryComp<NestingComponent>(approver, out var approverComp))
        {
            approverComp.NestPartner = uid;
            DoEnterNest(approver, approverComp);
            _popup.PopupEntity(Loc.GetString("nesting-shared-enter"), approver, approver, PopupType.Medium);
        }
    }

    private void DenyNesting(EntityUid uid, NestingComponent comp, EntityUid denier)
    {
        if (!comp.RequestingNest)
            return;
        comp.RequestingNest = false;
        Dirty(uid, comp);
        _popup.PopupEntity(Loc.GetString("nesting-partner-denied"), uid, uid, PopupType.MediumCaution);
    }

    private void OnEnterNest(EntityUid uid, NestingComponent comp, EnterNestActionEvent args)
    {
        if (comp.RequiresNestPartner)
        {
            var partners = GetNearbyResomis(uid, comp);
            if (partners.Count == 0)
            {
                _popup.PopupEntity(Loc.GetString("nesting-requires-partner"), uid, uid, PopupType.SmallCaution);
                return;
            }

            // Set pending state and notify nearby Resomis
            comp.RequestingNest = true;
            comp.NestRequestExpiry = _timing.CurTime + comp.NestRequestTimeout;
            Dirty(uid, comp);

            var name = (object) _entMan.GetComponent<MetaDataComponent>(uid).EntityName;
            foreach (var partner in partners)
                _popup.PopupEntity(Loc.GetString("nesting-request-received", ("name", name)), partner, partner, PopupType.Medium);

            _popup.PopupEntity(Loc.GetString("nesting-waiting-for-partner"), uid, uid, PopupType.Small);
            return;
        }

        DoEnterNest(uid, comp);
    }

    private void DoEnterNest(EntityUid uid, NestingComponent comp)
    {
        comp.IsNesting = true;
        comp.NextHeal = _timing.CurTime;

        EnsureComp<NestingFrozenComponent>(uid);

        _actions.RemoveAction(uid, comp.EnterNestActionEntity);
        _actions.AddAction(uid, ref comp.ExitNestActionEntity, comp.ExitNestAction);

        _audio.PlayPvs(comp.NestEnterSound, uid);

        var coords = Transform(uid).Coordinates;
        Spawn(comp.NestEnterEffect, coords);
        comp.ContinuousEffectEntity = Spawn(comp.NestContinuousEffect, coords);

        Dirty(uid, comp);

        var ev = new NestingAnimationEvent(GetNetEntity(uid), GetNetCoordinates(coords), NestingAnimationType.Enter);
        RaiseNetworkEvent(ev, Filter.Pvs(uid, entityManager: EntityManager));
    }

    private void OnExitNest(EntityUid uid, NestingComponent comp, ExitNestActionEvent args)
    {
        comp.IsNesting = false;

        // If sharing a nest, force the partner out too
        if (comp.NestPartner.HasValue)
        {
            var partner = comp.NestPartner.Value;
            comp.NestPartner = null;

            if (TryComp<NestingComponent>(partner, out var partnerComp) && partnerComp.IsNesting)
            {
                partnerComp.NestPartner = null;
                _popup.PopupEntity(Loc.GetString("nesting-partner-left"), partner, partner, PopupType.MediumCaution);
                RaiseLocalEvent(partner, new ExitNestActionEvent());
            }
        }

        RemComp<NestingFrozenComponent>(uid);

        _actions.RemoveAction(uid, comp.ExitNestActionEntity);
        _actions.AddAction(uid, ref comp.EnterNestActionEntity, comp.EnterNestAction);
        _actions.SetCooldown(comp.EnterNestActionEntity, comp.NestCooldown);

        _audio.PlayPvs(comp.NestExitSound, uid);

        var coords = Transform(uid).Coordinates;

        // Remove the continuous effect and spawn exit animation
        if (comp.ContinuousEffectEntity.HasValue)
        {
            QueueDel(comp.ContinuousEffectEntity.Value);
            comp.ContinuousEffectEntity = null;
        }

        Spawn(comp.NestExitEffect, coords);

        Dirty(uid, comp);

        var ev = new NestingAnimationEvent(GetNetEntity(uid), GetNetCoordinates(coords), NestingAnimationType.Exit);
        RaiseNetworkEvent(ev, Filter.Pvs(uid, entityManager: EntityManager));
    }
}
