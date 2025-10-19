using Content.Goobstation.Common.Traits.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.IdentityManagement;
using Content.Shared.InteractionVerbs;
using Content.Shared.InteractionVerbs.Events;
using Content.Shared.Standing;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Shared.Traits.Systems;

public sealed partial class SocialAnxietySystem : EntitySystem
{
    [Dependency] private readonly StandingStateSystem _standingSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedStunSystem _stunSystem = default!;

    private readonly ProtoId<InteractionVerbPrototype> _prototypeHug = "Hug";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SocialAnxietyComponent, InteractionSuccessEvent>(OnHug);
        SubscribeLocalEvent<InteractionVerbDoAfterEvent>(OnVerbHug);
    }
    private void OnHug(EntityUid uid, SocialAnxietyComponent component, ref InteractionSuccessEvent args)
    {
        _standingSystem.Down(uid);
        _stunSystem.TryStun(uid, TimeSpan.FromSeconds(component.DownedTime), true);
        var mobName = Identity.Name(uid, EntityManager);
        _popupSystem.PopupEntity(Loc.GetString("social-anxiety-hugged", ("user", mobName)), uid, PopupType.MediumCaution);
    }

    private void OnVerbHug(InteractionVerbDoAfterEvent args)
    {
        // we HAVE to subscribe to every InteractionVerbDoAfterEven that fires.
        // because I don't think absolute EE supercode allows us to do it normally.
        if (!TryComp<SocialAnxietyComponent>(args.Target, out var component))
            return;
        if (!args.Target.HasValue && args.VerbPrototype != _prototypeHug)
            return;
        var uid = args.Target.Value;
        _standingSystem.Down(uid);
        _stunSystem.TryStun(uid, TimeSpan.FromSeconds(component.DownedTime), true);
        var mobName = Identity.Name(uid, EntityManager);
        _popupSystem.PopupEntity(Loc.GetString("social-anxiety-hugged", ("user", mobName)), uid, PopupType.MediumCaution);
    }
}
