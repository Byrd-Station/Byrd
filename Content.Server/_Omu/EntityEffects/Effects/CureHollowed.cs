using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Content.Goobstation.Common.Changeling;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class CureHollowed : EntityEffect
{
    [DataField]

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entityManager = args.EntityManager;
        if (entityManager.HasComponent<AbsorbedComponent>(args.TargetEntity))
            return;

        entityManager.RemoveComponent<AbsorbedComponent>(args.TargetEntity);
        entityManager.RemoveComponent<UnrevivableComponent>(args.TargetEntity);
    }
}
