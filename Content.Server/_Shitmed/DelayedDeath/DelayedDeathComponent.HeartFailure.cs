// SPDX-FileCopyrightText: 2024 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Solstice <solsticeofthewinter@gmail.com>
// SPDX-FileCopyrightText: 2025 SolsticeOfTheWinter <solsticeofthewinter@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Shitmed.DelayedDeath;

public sealed partial class DelayedDeathComponent : Component
{

    /// <summary>
    /// Set to true if death caused by heart failure, duh.
    /// Important so we don't accidentally let a debrained guy walk away because of a fixed heart attack
    /// </summary>
    [DataField]
    public bool FromHeartFailure = false;
}
