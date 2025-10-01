// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aidenkrz <aiden@djkraz.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Lincoln McQueen <lincoln.mcqueen@gmail.com>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 Tim <timfalken@hotmail.com>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;
using Content.Goobstation.Shared.MartialArts;
using Content.Goobstation.Shared.MartialArts.Components;
using Content.Goobstation.Shared.MartialArts.Events;
using Content.Shared.Body.Systems;
using Content.Server.Body.Components;
using Content.Shared.Body.Organ;
using Content.Server.Chat.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Traumas;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Chat;
//using Content.Shared.Damage;

namespace Content.Goobstation.Server.MartialArts;

/// <summary>
/// Just handles carp sayings for now.
/// </summary>
public sealed partial class MartialArtsSystem : SharedMartialArtsSystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly TraumaSystem _trauma = default!;
    //[Dependency] private readonly DamageableSystem _damagable = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CanPerformComboComponent, SleepingCarpSaying>(OnSleepingCarpSaying);
        SubscribeLocalEvent<BodyComponent, KravMagaLungPunchEvent>(OnLungPunch);
    }

    private void OnSleepingCarpSaying(Entity<CanPerformComboComponent> ent, ref SleepingCarpSaying args)
    {
        _chat.TrySendInGameICMessage(ent, Loc.GetString(args.Saying), InGameICChatType.Speak, false);
    }

    private void OnLungPunch(Entity<BodyComponent> ent, ref KravMagaLungPunchEvent args)
    {
        var targetUid = GetEntity(args.Target);
        if (!_body.TryGetBodyOrganEntityComps<LungComponent>(ent!, out var lungs))
        {
            return;
        }
        // Cool, I have a list of lungs now! I should destroy each lung one by one, but skip the ones totally destroyed.
        // Get the stomach with the highest available solution volume
        FixedPoint2 lowestAvailable = 100;
        Entity<LungComponent, OrganComponent>? lungToUse = null;
        foreach (var lung in lungs)
        {
            //So... skip destroyed lungs, and then continuously compare each lung's max severy with the last worst.

            if ( lung.Comp2.OrganSeverity is OrganSeverity.Destroyed )
                continue;

            if (lung.Comp2.IntegrityCap >= lowestAvailable)
                continue;

            lungToUse = lung;
            lowestAvailable = lung.Comp2.IntegrityCap;
        }
        // Lung to obliterate found, now to do the funny.

        if (lungToUse == null)
            return;

        // This shit doesn't work because OrganDamage is not a damn damage type, it's a trauma *inflicted by* a damage type.
        //_damagable.TryChangeDamage(lungToUse.Value,args.LungDamage,true,true);
        if (!_trauma.TryChangeOrganDamageModifier(lungToUse.Value.Owner,
                -1,
                ent.Owner,
                "Crunched",
                lungToUse.Value.Comp2))
        {
            _trauma.TryCreateOrganDamageModifier(lungToUse.Value.Owner,
                -1,
                ent.Owner,
                "Crunched",
                lungToUse.Value.Comp2);
        }
    }
}
