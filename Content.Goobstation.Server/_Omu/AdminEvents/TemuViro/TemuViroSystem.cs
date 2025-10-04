// SPDX-FileCopyrightText: 2025 RichardBlonski <48651647+RichardBlonski@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared._Omu.AdminEvents.TemuViro;
using Content.Goobstation.Shared._Omu.AdminEvents.TemuViro.Components;
using Content.Goobstation.Shared._Omu.AdminEvents.TemuViro.Events;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Server.Medical;
using Content.Shared.Mobs.Systems;

namespace Content.Goobstation.Server._Omu.AdminEvents.TemuViro;

public sealed class TemuViroSystem : SharedTemuViroSystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly VomitSystem _vomitSystem = default!;



    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TemuViroComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<TemuViroComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<TemuViroComponent, OnVomitEvent>(OnVomit);
    }

    private void OnMapInit(EntityUid uid, TemuViroComponent component, MapInitEvent args)
    {
        // Admin Logging
        _adminLogManager.Add(LogType.AdminMessage,
            LogImpact.Extreme,
            $"{ToPrettyString(uid)} has been infected with Temu Virus");
    }

    #region Solution Container Events / Cure Handling (When injected)
    private void OnSolutionChanged(Entity<TemuViroComponent> entity, ref SolutionContainerChangedEvent args)
    {
        var (uid, component) = entity;

        // Ensure the entity is not cured
        if (entity.Comp.IsCured)
            return;

        // Only process if this is NOT a change to the bloodstream
        if (args.SolutionId == "bloodstream")
            return;

        // Get the solution from the event args
        var solution = args.Solution;
        if (solution is null)
            return;

        // Get the current amount of cure chemical in the solution
        var currentAmount = solution.GetTotalPrototypeQuantity(component.CureChemical);
        // Process the cure chemical if present
        if (currentAmount > 0)
            ProcessCureChemical(uid, component.CureChemical, (float)currentAmount, component);
    }

    public void ProcessCureChemical(EntityUid uid, string reagentId, float amount, TemuViroComponent? component = null)
    {
        if (!Resolve(uid, ref component) || component.IsCured)
            return;

        // Increase cure progress
        component.CureProgress += amount;
        // Check if cured
        if (component.CureProgress >= component.CureAmountNeeded)
        {
            if (!EntityManager.EntityExists(uid))
                return;

            // Admin Log
            _adminLogManager.Add(LogType.AdminMessage,
                LogImpact.Medium,
                $"{ToPrettyString(uid)} has been cured of Temu Virus");

            component.IsCured = true;

            var netEntity = GetNetEntity(uid);
            var ev = new OnCuredEvent(netEntity, true);
            RaiseLocalEvent(uid, ref ev);
        }
    }
    #endregion

    #region Vomit
    private void OnVomit(Entity<TemuViroComponent> entity, ref OnVomitEvent args)
    {
        var target = GetEntity(args.Entity);

        if (!TryComp<TemuViroComponent>(target, out var comp) ||
            comp.IsCured ||
            !_mobStateSystem.IsAlive(target))
        {
            return;
        }

        _vomitSystem.Vomit(target, 5f, 1f);
    }
    #endregion
}
