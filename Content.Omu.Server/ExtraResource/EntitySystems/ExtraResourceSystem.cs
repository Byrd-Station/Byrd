// SPDX-FileCopyrightText: 2026 Raze500
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Omu.Shared.ExtraResource.Components;
using Content.Omu.Shared.Resomi.Components;
using Content.Shared.Alert;
using Content.Shared.Damage.Components;
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

    // restfulness drains whenever stamina damage is above this threshold (10 = below 90% stamina)
    private const float RestfulnessStaminaDrainThreshold = 10f;

    // how fast restfulness drains per second when stamina is low
    private const float RestfulnessDrainRate = 100f / (15f * 60f); // empties in ~15 min

    // how fast restfulness recharges per second when stamina is full
    private const float RestfulnessRechargeRate = 100f / (10f * 60f); // fills in ~10 min

    // how fast restfulness recharges per second while nesting (much faster)
    private const float RestfulnessNestRechargeRate = 100f / (2f * 60f); // fills in ~2 min

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
            // nesting recharges restfulness fast
            delta = RestfulnessNestRechargeRate * frameTime;
        }
        else if (TryComp<StaminaComponent>(uid, out var stamina) && stamina.StaminaDamage > RestfulnessStaminaDrainThreshold)
        {
            // stamina is low - drain restfulness
            delta = -RestfulnessDrainRate * frameTime;
        }
        else
        {
            // stamina is full - slowly recharge
            delta = RestfulnessRechargeRate * frameTime;
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
    ///     called by NestingSystem to instantly apply a restfulness burst on nest enter/exit.
    ///     the half-second wind-up is handled by NestingSystem before calling this.
    /// </summary>
    public void OnNestingStateChanged(EntityUid uid, ExtraResourceComponent comp, bool isNesting)
    {
        // the actual recharge happens in Update via the NestingFrozenComponent check.
        // this just triggers a speed refresh so the modifier applies immediately.
        _movementSpeed.RefreshMovementSpeedModifiers(uid);
    }
}
