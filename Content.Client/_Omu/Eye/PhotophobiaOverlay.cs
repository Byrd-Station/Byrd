using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Content.Shared._Omu.Traits.Assorted;
using Content.Shared.Eye.Blinding.Components;
using Robust.Shared.Configuration;
using System.Numerics;

namespace Content.Client._Omu.Eye
{
    public sealed class PhotophobiaOverlay : Overlay
    {
        [Dependency] private readonly IClyde _clyde = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IConfigurationManager _configManager = default!;

        // public override bool RequestScreenTexture => true;
        public override OverlaySpace Space => OverlaySpace.WorldSpace;
        private readonly ShaderInstance _photophobiaShader;

        public PhotophobiaOverlay()
        {
            IoCManager.InjectDependencies(this);
            _photophobiaShader = _prototypeManager.Index<ShaderPrototype>("PhotophobiaShader").InstanceUnique();
        }

        protected override bool BeforeDraw(in OverlayDrawArgs args)
        {
            if (!_entityManager.TryGetComponent(_playerManager.LocalSession?.AttachedEntity, out EyeComponent? eyeComp))
                return false;

            if (args.Viewport.Eye != eyeComp.Eye)
                return false;

            var playerEntity = _playerManager.LocalSession?.AttachedEntity;

            if (playerEntity == null)
                return false;

            if (!_entityManager.TryGetComponent<PhotophobiaComponent>(playerEntity, out var blurComp))
                return false;

            // if (blurComp.Magnitude <= 0)
            //    return false;

            return true;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            //if (ScreenTexture == null)
            //    return;

            var playerEntity = _playerManager.LocalSession?.AttachedEntity;

            var worldHandle = args.WorldHandle;
            var viewport = args.WorldBounds;

            // _photophobiaShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            _photophobiaShader.SetParameter("LIGHT_TEXTURE", args.Viewport.LightRenderTarget.Texture);

            worldHandle.SetTransform(Matrix3x2.Identity);
            worldHandle.UseShader(_photophobiaShader);
            worldHandle.DrawRect(viewport, Color.White);
            worldHandle.UseShader(null);

        }
    }
}