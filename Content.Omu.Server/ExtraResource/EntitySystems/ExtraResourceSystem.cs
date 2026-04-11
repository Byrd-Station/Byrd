// SPDX-FileCopyrightText: 2026 Raze500
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Omu.Shared.ExtraResource.Components;
using Content.Omu.Shared.Resomi.Components;
using Content.Shared.Alert;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Omu.Server.ExtraResource.EntitySystems;

/// <summary>
///     manages all ExtraResourceComponent entries each tick.
///     currently handles the "restfulness" key for Resomi (and any future species that use it).
///     to add a new resource for another species, add a new key check in Update.
/// </summary>
public sealed class ExtraResourceSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;

    // drains passively at all times when not nesting (~8 min to empty)
    private const float RestfulnessDrainRate = 100f / (8f * 60f);

    // recharges while nesting (~4 min to fill)
    private const float RestfulnessNestRechargeRate = 100f / (4f * 60f);

    // sprint speed boost when fully rested (above 80%)
    private const float RestfulSprintBoost = 1.15f;

    // sprint speed penalty when exhausted (below 30%)
    private const float ExhaustedSprintPenalty = 0.80f;

    // thresholds for the sprint modifier transitions
    private const float RestfulThreshold = 80f;
    private const float ExhaustedThreshold = 30f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExtraResourceComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ExtraResourceComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            foreach (var key in comp.Resources.Keys)
            {
                switch (key)
                {
                    case "restfulness":
                        UpdateRestfulness(uid, comp, comp.Resources[key], frameTime);
                        break;
                }
            }
        }
    }

    private void UpdateRestfulness(EntityUid uid, ExtraResourceComponent comp, ExtraResourceEntry entry, float frameTime)
    {
        var isNesting = HasComp<NestingFrozenComponent>(uid);

        float delta;
        if (isNesting)
        {
            // nesting recharges restfulness
            delta = RestfulnessNestRechargeRate * frameTime;
        }
        else
        {
            // always drains when not nesting - restfulness is only recovered by resting
            delta = -RestfulnessDrainRate * frameTime;
        }

        var previous = entry.Current;
        entry.Current = Math.Clamp(entry.Current + delta, 0f, entry.Max);

        // update alert severity
        UpdateAlert(uid, entry);

        // refresh sprint speed if restfulness crossed a threshold
        var crossedThreshold =
            (previous >= RestfulThreshold) != (entry.Current >= RestfulThreshold) ||
            (previous <= ExhaustedThreshold) != (entry.Current <= ExhaustedThreshold);

        if (crossedThreshold)
            _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }

    private void UpdateAlert(EntityUid uid, ExtraResourceEntry entry)
    {
        if (entry.Alert == default)
            return;

        // severity goes from 0 (full) to max (empty) - the alert system handles the icon
        var severity = (short) Math.Round((1f - entry.Current / entry.Max) * 7f);
        _alerts.ShowAlert(uid, entry.Alert, severity);
    }

    private void OnRefreshSpeed(Entity<ExtraResourceComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!ent.Comp.Resources.TryGetValue("restfulness", out var entry))
            return;

        // only sprint is affected, not walking
        if (entry.Current >= RestfulThreshold)
            args.ModifySpeed(1f, RestfulSprintBoost);
        else if (entry.Current <= ExhaustedThreshold)
            args.ModifySpeed(1f, ExhaustedSprintPenalty);
        // between thresholds = no modifier
    }

    /// <summary>
    ///     called by NestingSystem when nesting state changes to trigger an immediate speed refresh.
    /// </summary>
    public void OnNestingStateChanged(EntityUid uid, ExtraResourceComponent comp, bool isNesting)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }
}
