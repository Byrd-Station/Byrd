using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Stunnable;

using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Pulling.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;

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
