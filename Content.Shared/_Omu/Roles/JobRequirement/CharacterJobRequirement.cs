using System.Diagnostics.CodeAnalysis;
using Content.Shared.Preferences;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using Content.Shared.Roles;
using System.Linq;


namespace Content.Shared._Omu.Roles;

/// <summary>
///     Requires the selected job to be one of the specified jobs
/// </summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterJobRequirement : JobRequirement
{
    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Jobs;

    public override bool Check(
        IEntityManager entManager,
        IPrototypeManager protoManager,
        HumanoidCharacterProfile? profile,
        IReadOnlyDictionary<string, TimeSpan> playTimes,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        reason = new FormattedMessage();

        if (profile is null)
            return true;

        var selectedjob = profile.JobPriorities
            .FirstOrDefault(p => p.Value == JobPriority.High).Key;

        // if no high role they'll be assistant so just treat it as such.
        if (EqualityComparer<ProtoId<JobPrototype>>.Default.Equals(selectedjob, default))
            selectedjob = "JobPassenger";

        if ((Inverted && !Jobs.Contains(selectedjob)) ||
            (!Inverted && Jobs.Contains(selectedjob) ))
            return true;

        reason = FormattedMessage.FromMarkupPermissive(
            !Inverted
                ? Loc.GetString(
                    "role-timer-whitelisted-job",
                    ("requiredjob", string.Join(", ",
                        Jobs.Select(j => Loc.GetString(protoManager.Index(j).Name)))))
                : Loc.GetString(
                    "role-timer-blacklisted-job",
                    ("selectedjob", Loc.GetString(protoManager.Index(selectedjob).Name)))
        );
        return false;
    }
}
