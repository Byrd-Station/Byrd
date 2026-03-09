namespace Content.Shared._Omu.Saboteur
{
    public interface ISaboteurOperationSystem
    {
        bool AssignObjectiveFromWeightedGroup(EntityUid saboteur, string groupId);
    }
}
