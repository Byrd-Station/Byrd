// SPDX-FileCopyrightText: 2024 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Body.Events;
using Content.Server.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Server._Shitmed.DelayedDeath;
using Content.Shared.Alert;

namespace Content.Server._Shitmed.Body.Organ;

public sealed class HeartSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _bodySystem = default!;
    [Dependency] private readonly AlertsSystem _alert = default!;

    private string _faultyHeartAlertId = "FaultyHeart";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HeartComponent, OrganAddedToBodyEvent>(HandleAddition);
        SubscribeLocalEvent<HeartComponent, OrganRemovedFromBodyEvent>(HandleRemoval);
        SubscribeLocalEvent<HeartComponent, OrganDisabledEvent>(OnOrganDisabled);
        SubscribeLocalEvent<HeartComponent, OrganEnabledEvent>(OnOrganEnabled);
    }

    private void HandleRemoval(EntityUid uid, HeartComponent _, ref OrganRemovedFromBodyEvent args)
    {
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(args.OldBody))
            return;

        // TODO: Add some form of very violent bleeding effect.
        EnsureComp<DelayedDeathComponent>(args.OldBody);
        _alert.ShowAlert(args.OldBody, _faultyHeartAlertId);
    }

    private void HandleAddition(EntityUid uid, HeartComponent _, ref OrganAddedToBodyEvent args)
    {
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(args.Body))
            return;

        if (_bodySystem.TryGetBodyOrganEntityComps<BrainComponent>(args.Body, out var _))
        {
            RemComp<DelayedDeathComponent>(args.Body);
            _alert.ClearAlert(args.Body, _faultyHeartAlertId);
        }
    }

    // Heartfailure time
    private void OnOrganDisabled(Entity<HeartComponent> ent, ref OrganDisabledEvent args)
    {
        var deth = EnsureComp<DelayedDeathComponent>(args.Organ.Owner);
        deth.FromHeartFailure = true;
        _alert.ShowAlert(args.Organ.Owner, _faultyHeartAlertId);
    }

    private void OnOrganEnabled(Entity<HeartComponent> ent, ref OrganEnabledEvent args)
    {
        if (TryComp<DelayedDeathComponent>(args.Organ.Owner, out var death)
            && death.FromHeartFailure)
        {
            RemComp<DelayedDeathComponent>(args.Organ.Owner);
            _alert.ClearAlert(args.Organ.Owner, _faultyHeartAlertId);
        }
    }
    // Shitmed-End
}
