using Content.Server._Omu.StationEvents.Components;
using Content.Server._Omu.Thaven;
using Content.Server.StationEvents.Events;
using Content.Shared._Omu.Thaven.Components;
using Content.Shared.GameTicking.Components;

namespace Content.Server._Omu.StationEvents.Events;

public sealed class ThavenMoodUpset : StationEventSystem<ThavenMoodUpsetRuleComponent>
{
    [Dependency] private readonly ThavenMoodsSystem _thavenMoods = default!;

    protected override void Started(EntityUid uid, ThavenMoodUpsetRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);

        var thavens = EntityQueryEnumerator<ThavenMoodsComponent>();
        while (thavens.MoveNext(out var thavenUid, out var thavenComp))
        {
            _thavenMoods.AddWildcardMood((thavenUid, thavenComp));
        }
    }
}

