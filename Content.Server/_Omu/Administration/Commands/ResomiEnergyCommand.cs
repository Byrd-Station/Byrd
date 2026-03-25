// SPDX-FileCopyrightText: 2026 Raze500
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Movement.Systems;
using Content.Shared._Omu.Resomi.Components;
using Robust.Shared.Console;

namespace Content.Server._Omu.Administration.Commands;

/// <summary>
/// Debug command to set a Resomi's energy level for testing.
/// Usage: resomienergy [entityUid] [0-100]
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class ResomiEnergyCommand : LocalizedEntityCommands
{
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;

    public override string Command => "resomienergy";
    public override string Description => "Set a Resomi's energy level (0-100) for testing.";
    public override string Help => "Usage: resomienergy <entityUid> <amount>";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netUid) || !EntityManager.TryGetEntity(netUid, out var uid))
        {
            shell.WriteError("Invalid entity UID.");
            return;
        }

        if (!float.TryParse(args[1], out var amount))
        {
            shell.WriteError("Amount must be a number (0-100).");
            return;
        }

        if (!EntityManager.TryGetComponent<ResomiEnergyComponent>(uid, out var comp))
        {
            shell.WriteError("Entity does not have ResomiEnergyComponent.");
            return;
        }

        comp.Energy = Math.Clamp(amount, 0f, comp.MaxEnergy);
        comp.IsExhausted = comp.Energy <= 0f;
        _speed.RefreshMovementSpeedModifiers(uid.Value);
        shell.WriteLine($"Set {EntityManager.GetComponent<MetaDataComponent>(uid.Value).EntityName} energy to {comp.Energy:F1}/{comp.MaxEnergy}.");
    }
}