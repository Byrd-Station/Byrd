// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Shared.Actions;
using Content.Omu.Shared.Resomi.Components;
using Content.Omu.Shared.Resomi.Events;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Omu.Server.Resomi.EntitySystems;

/// <summary>
///     handles the Resomi chirp ability.
///     when a Resomi calls, nearby Resomis get a cursor popup (not logged to chat)
///     with the direction, plus a sound after a short delay.
///     the call is logged for admins.
/// </summary>
public sealed class ResomiCallSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly SoundSpecifier CallSenderSound = new SoundPathSpecifier("/Audio/_Starlight/Voice/Resomi/resomi_call_1.ogg");

    private static readonly SoundSpecifier[] CallReceiverSounds =
    [
        new SoundPathSpecifier("/Audio/_Starlight/Voice/Resomi/resomi_call_1.ogg"),
        new SoundPathSpecifier("/Audio/_Starlight/Voice/Resomi/resomi_call_2.ogg"),
    ];

    // maximum range in tiles at which other Resomis can hear the call
    private const float CallRange = 45f;

    // delay before the receiver gets the sound - feels like the signal is traveling
    private static readonly TimeSpan ReceiverDelay = TimeSpan.FromSeconds(1);

    // pending sounds queued with a delivery time
    private readonly List<PendingCallNotification> _pending = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<ResomiCallComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ResomiCallComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ResomiCallComponent, ResomiCallActionEvent>(OnCall);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var notification = _pending[i];
            if (now < notification.DeliverAt)
                continue;

            if (!Deleted(notification.ReceiverUid))
            {
                // cursor popup does not get logged to chat regardless of the LogInChat setting
                _popup.PopupCursor(notification.Message, notification.ReceiverUid, PopupType.Large);
                _audio.PlayEntity(notification.Sound, Filter.Entities(notification.ReceiverUid), notification.ReceiverUid, true, AudioParams.Default.WithVolume(-4f));
            }

            _pending.RemoveAt(i);
        }
    }

    private void OnMapInit(EntityUid uid, ResomiCallComponent comp, MapInitEvent args)
    {
        _actions.AddAction(uid, ref comp.CallActionEntity, comp.CallAction);
    }

    private void OnShutdown(EntityUid uid, ResomiCallComponent comp, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, comp.CallActionEntity);
    }

    private void OnCall(EntityUid uid, ResomiCallComponent comp, ResomiCallActionEvent args)
    {
        var callerName = MetaData(uid).EntityName;
        var callerXform = Transform(uid);
        var callerPos = _transform.GetWorldPosition(callerXform);

        // cursor popup for the caller - not logged to chat
        _popup.PopupCursor(Loc.GetString("resomi-call-sent"), uid, PopupType.Large);
        _audio.PlayEntity(CallSenderSound, Filter.Entities(uid), uid, true, AudioParams.Default.WithVolume(-2f));

        // log for admins so they can track species communication
        _adminLog.Add(LogType.Chat, LogImpact.Low,
            $"{ToPrettyString(uid):entity} used ResomiCall (chirp) at {callerPos}");

        var deliverAt = _timing.CurTime + ReceiverDelay;

        var query = EntityQueryEnumerator<ResomiCallComponent>();
        while (query.MoveNext(out var receiverUid, out _))
        {
            if (receiverUid == uid)
                continue;

            var receiverXform = Transform(receiverUid);
            if (receiverXform.MapID != callerXform.MapID)
                continue;

            var receiverPos = _transform.GetWorldPosition(receiverXform);
            if ((callerPos - receiverPos).Length() > CallRange)
                continue;

            var direction = GetCompassDirection(receiverPos, callerPos);
            var msg = Loc.GetString("resomi-call-received", ("name", callerName), ("direction", direction));

            _pending.Add(new PendingCallNotification(
                receiverUid,
                msg,
                _random.Pick(CallReceiverSounds),
                deliverAt
            ));
        }

        _actions.SetCooldown(comp.CallActionEntity, comp.CallCooldown);
    }

    private string GetCompassDirection(Vector2 from, Vector2 to)
    {
        var diff = to - from;

        if (diff.LengthSquared() < 0.5f)
            return Loc.GetString("resomi-call-direction-nearby");

        // absolute world-space direction - north is always station north
        var angle = Math.Atan2(diff.Y, diff.X) * (180.0 / Math.PI);
        if (angle < 0) angle += 360.0;

        return angle switch
        {
            >= 337.5 or < 22.5   => Loc.GetString("resomi-call-direction-east"),
            >= 22.5  and < 67.5  => Loc.GetString("resomi-call-direction-northeast"),
            >= 67.5  and < 112.5 => Loc.GetString("resomi-call-direction-north"),
            >= 112.5 and < 157.5 => Loc.GetString("resomi-call-direction-northwest"),
            >= 157.5 and < 202.5 => Loc.GetString("resomi-call-direction-west"),
            >= 202.5 and < 247.5 => Loc.GetString("resomi-call-direction-southwest"),
            >= 247.5 and < 292.5 => Loc.GetString("resomi-call-direction-south"),
            _                    => Loc.GetString("resomi-call-direction-southeast"),
        };
    }

    private sealed record PendingCallNotification(
        EntityUid ReceiverUid,
        string Message,
        SoundSpecifier Sound,
        TimeSpan DeliverAt
    );
}
