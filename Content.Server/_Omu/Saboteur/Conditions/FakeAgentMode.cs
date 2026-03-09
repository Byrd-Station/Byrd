// SPDX-FileCopyrightText: 2026 Eponymic-sys
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Server._Omu.Saboteur.Conditions;

/// <summary>
/// Specifies the strategy for <see cref="Components.SaboteurFakeAgentConditionComponent"/>.
/// </summary>
public enum FakeAgentMode
{
    /// <summary>
    /// The saboteur themselves must be the only person on the station
    /// with a (fake) mindshield implant.
    /// </summary>
    SelfSoleHolder,

    /// <summary>
    /// A mind-controlled puppet must hold a fake mindshield matching a
    /// specific target job from <see cref="Components.SaboteurFakeAgentConditionComponent.TargetJobs"/>.
    /// </summary>
    PuppetInJob,

    /// <summary>
    /// Every active job role on the station must have at least one fake-
    /// mindshielded crew member covering it.
    /// </summary>
    CoverAllJobs,
}
