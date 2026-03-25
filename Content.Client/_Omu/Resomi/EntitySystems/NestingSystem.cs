using Content.Shared.Popups;
using Content.Shared._Omu.Resomi.EntitySystems;
using Content.Shared._Omu.Resomi.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Client._Omu.Resomi.EntitySystems;

/// <summary>
/// Client-side system for Resomi nesting — shows popups when entering/exiting.
/// </summary>
public sealed class NestingSystem : SharedNestingSystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<NestingAnimationEvent>(OnNestingAnimation);
    }

    private void OnNestingAnimation(NestingAnimationEvent ev)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var entity = GetEntity(ev.Entity);

        switch (ev.AnimationType)
        {
            case NestingAnimationType.Enter:
                _popup.PopupEntity(Loc.GetString("nesting-enter"), entity, PopupType.Medium);
                break;
            case NestingAnimationType.Exit:
                _popup.PopupEntity(Loc.GetString("nesting-exit"), entity, PopupType.Medium);
                break;
        }
    }
}
