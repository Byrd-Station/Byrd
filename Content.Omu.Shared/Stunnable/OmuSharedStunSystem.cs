using Content.Shared.Stunnable;
using Content.Shared.Pulling.Events;

namespace Content.Omu.Shared.Stunnable;

public sealed class OmuSharedStunSystem : EntitySystem
{
    
    public override void Initialize()
    {
        // Attempt event subscriptions.
        SubscribeLocalEvent<StunnedComponent, AttemptStopPullingEvent>(HandleStopPull);
    }

    #region Attempt Event Handling
    
    private void HandleStopPull(EntityUid uid, StunnedComponent _, ref AttemptStopPullingEvent args)
    {
        if (args.User == null || !Exists(args.User.Value))
            return;

        if (args.User.Value == uid)
        {
            //TODO: UX feedback. Simply blocking the normal interaction feels like an interface bug

            args.Cancelled = true;
        }

    }

    #endregion
}
