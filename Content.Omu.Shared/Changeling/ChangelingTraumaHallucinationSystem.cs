using Content.Omu.Common.Changeling;
using Content.Shared.Examine;

namespace Content.Omu.Shared.Changeling;

public sealed class ChangelingTraumaHallucinationSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<HollowKillerAddedEvent> (OnExamine);
    }



}
