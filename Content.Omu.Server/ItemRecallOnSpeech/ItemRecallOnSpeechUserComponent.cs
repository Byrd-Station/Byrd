namespace Content.Omu.Server.ItemRecallOnSpeech;

[RegisterComponent]
public sealed partial class ItemRecallOnSpeechUserComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public List<Entity<ItemRecallOnSpeechComponent>> ItemsToRecall = [];
}
