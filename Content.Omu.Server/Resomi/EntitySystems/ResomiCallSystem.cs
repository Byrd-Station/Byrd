// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Audio;
using Content.Shared.Popups;
using Content.Omu.Shared.Resomi.Components;
using Content.Omu.Shared.Resomi.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Omu.Server.Resomi.EntitySystems;

/// <summary>
/// Handles the Resomi Calling ability.
/// When a Resomi calls, nearby Resomis (within CallRange tiles) receive a
/// hivemind-style directional alert only visible to their species.
/// </summary>
public sealed class ResomiCallSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly SoundSpecifier CallSenderSound = new SoundPathSpecifier("/Audio/_Starlight/Voice/Resomi/resomi_call_1.ogg");

    private static readonly SoundSpecifier[] CallReceiverSounds =
    [
        new SoundPathSpecifier("/Audio/_Starlight/Voice/Resomi/resomi_call_1.ogg"),
        new SoundPathSpecifier("/Audio/_Starlight/Voice/Resomi/resomi_call_2.ogg"),
    ];

    /// <summary>Maximum range in tiles at which other Resomis can hear the call.</summary>
    private const float CallRange = 45f;

    public override void Initialize()
    {
        SubscribeLocalEvent<ResomiCallComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ResomiCallComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ResomiCallComponent, ResomiCallActionEvent>(OnCall);
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

        // Confirm to the caller + play call sound at caller's position
        _popup.PopupEntity(Loc.GetString("resomi-call-sent"), uid, PopupType.Small);
        _audio.PlayEntity(CallSenderSound, Filter.Entities(uid), uid, true, AudioParams.Default.WithVolume(-2f));

        // Notify nearby Resomis only
        var query = EntityQueryEnumerator<ResomiCallComponent>();
        while (query.MoveNext(out var receiverUid, out _))
        {
            if (receiverUid == uid)
                continue;

            var receiverXform = Transform(receiverUid);
            if (receiverXform.MapID != callerXform.MapID)
                continue;

            var receiverPos = _transform.GetWorldPosition(receiverXform);
            var dist = (callerPos - receiverPos).Length();

            if (dist > CallRange)
                continue;

            var direction = GetCompassDirection(receiverPos, callerPos);

            var msg = Loc.GetString("resomi-call-received",
                ("name", callerName),
                ("direction", direction));

            // Send as a private server-side popup visible ONLY to this Resomi
            _popup.PopupEntity(msg, receiverUid, Filter.Entities(receiverUid), true, PopupType.MediumCaution);

            // Play a random call sound privately to the receiver only
            var sound = _random.Pick(CallReceiverSounds);
            _audio.PlayEntity(sound, Filter.Entities(receiverUid), receiverUid, true, AudioParams.Default.WithVolume(-4f));
        }

        _actions.SetCooldown(comp.CallActionEntity, comp.CallCooldown);
    }

    private string GetCompassDirection(Vector2 from, Vector2 to)
    {
        var diff = to - from;

        if (diff.LengthSquared() < 0.5f)
            return Loc.GetString("resomi-call-direction-nearby");

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
}
