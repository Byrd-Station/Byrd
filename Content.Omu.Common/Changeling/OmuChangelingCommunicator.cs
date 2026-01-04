namespace Content.Omu.Common.Changeling;

public abstract class OmuChangelingCommunicator : EntitySystem
{
    public abstract void RemoveOrgansOnAbsorb(EntityUid target);
}
