using Content.Shared._Omu.Traits;
using Content.Shared._Omu.Traits.Assorted;
using Content.Client._Omu.Eye;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._Omu.Traits;

public sealed class PhotophobiaSystem : SharedPhotophobiaSystem
{

    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    private PhotophobiaOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotophobiaComponent, ComponentInit>(OnPhotophobiaInit);
        SubscribeLocalEvent<PhotophobiaComponent, ComponentShutdown>(OnPhotophobiaShutdown);

        SubscribeLocalEvent<PhotophobiaComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PhotophobiaComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        _overlay = new();
    }

    private void OnPlayerAttached(EntityUid uid, PhotophobiaComponent component, LocalPlayerAttachedEvent args)
    {
        _overlayMan.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(EntityUid uid, PhotophobiaComponent component, LocalPlayerDetachedEvent args)
    {
        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnPhotophobiaInit(EntityUid uid, PhotophobiaComponent component, ComponentInit args)
    {
        if (_player.LocalEntity == uid)
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnPhotophobiaShutdown(EntityUid uid, PhotophobiaComponent component, ComponentShutdown args)
    {
        if (_player.LocalEntity == uid)
        {
            _overlayMan.RemoveOverlay(_overlay);
        }
    }

}