// SPDX-FileCopyrightText: 2026 Space Station 14 Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Emp;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Content._Omu.Shared.Cybernetics;
using Content.Shared._Shitmed.Damage;
using Content.Shared._Shitmed.Targeting;

namespace Content._Omu.Server.Cybernetics;

internal sealed class CyberneticsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    public const int IonDamageDealt = 200;
    public override void Initialize()
    {
        SubscribeLocalEvent<EmpVulnerableComponent, EmpPulseEvent>(OnEmpPulse);
        SubscribeLocalEvent<EmpVulnerableComponent, EmpDisabledRemoved>(OnEmpDisabledRemoved);
    }
    private void OnEmpPulse(Entity<EmpVulnerableComponent> cyberEnt, ref EmpPulseEvent ev)
    {
        if (cyberEnt.Comp.Disabled)
            return;

        ev.Affected = true;
        ev.Disabled = true;

        cyberEnt.Comp.Disabled = true;

        if (!TryComp(cyberEnt, out DamageableComponent? damageable))
            return;
        // Vital damage caused by EMP. This damage is spread across every limb.
        var ion = new DamageSpecifier(_prototypes.Index<DamageTypePrototype>("Ion"), IonDamageDealt);
        _damageable.TryChangeDamage(cyberEnt, ion, ignoreResistances: true, targetPart: TargetBodyPart.All, splitDamage: SplitDamageBehavior.SplitEnsureAll, damageable: damageable);
        Dirty(cyberEnt, damageable);
    }

    private void OnEmpDisabledRemoved(Entity<EmpVulnerableComponent> cyberEnt, ref EmpDisabledRemoved ev)
    {
        if (cyberEnt.Comp.Disabled)
            cyberEnt.Comp.Disabled = false;
    }
}
