namespace Content.Omu.Common.Changeling;

public abstract class OmuChangelingCommunicator : EntitySystem
{
    public abstract void SetupLingData(Entity<Component> ling, EntityUid lingMind, EntityUid target);

    public abstract void RemoveOrgansOnAbsorb(EntityUid target);
}
