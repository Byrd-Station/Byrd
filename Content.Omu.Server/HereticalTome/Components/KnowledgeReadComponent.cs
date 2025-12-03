namespace Content.Omu.Server.HereticalTome.Components;

[RegisterComponent]
public sealed partial class KnowledgeReadComponent : Component
{
    /// <summary>
    /// The knowledge given by the book
    /// </summary>
    [DataField]
    public float KnowledgeBook = 1f;
}
