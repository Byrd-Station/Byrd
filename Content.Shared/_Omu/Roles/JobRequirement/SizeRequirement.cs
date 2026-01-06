using System.Diagnostics.CodeAnalysis;
using Content.Shared.Preferences;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using Content.Shared.Roles;

namespace Content.Shared._Omu.Roles;

/// <summary>
/// Requires a character to have a certain height
/// </summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class SizeRequirement : JobRequirement
{
    [DataField(required: true)]
    public float MinimumHeight = 0;

    public override bool Check(
        IEntityManager entManager,
        IPrototypeManager protoManager,
        HumanoidCharacterProfile? profile,
        IReadOnlyDictionary<string, TimeSpan> playTimes,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        reason = new FormattedMessage();

        //the profile could be null if the player is a ghost. In this case we don't need to block the role selection for ghostrole
        if (profile is null)
            return true;

        var species = protoManager.Index(profile.Species);
        var height = MathF.Round(species.AverageHeight * profile.Height);

        if (!Inverted)
        {
            reason = FormattedMessage.FromMarkupPermissive(Loc.GetString("role-timer-too-short", ("height", MinimumHeight)));
            return height >= MinimumHeight;
        }
        else
        {
            reason = FormattedMessage.FromMarkupPermissive(Loc.GetString("role-timer-too-tall", ("weight", MinimumHeight)));
            return height <= MinimumHeight;
        }
    }
}
