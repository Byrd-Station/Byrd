using Content.Shared.Heretic;
using Content.Omu.Server.HereticalTome.Components;
using Content.Server.Heretic.EntitySystems;
using Content.Shared.Interaction.Events;

namespace Content.Omu.Server.HereticalTome;

public sealed class KnowledgeReadSystem : EntitySystem
{
    [Dependency] private readonly HereticSystem _heretic = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KnowledgeReadComponent, UseInHandEvent>(OnTriggerBooks);
    }
    private void OnTriggerBooks(EntityUid book, KnowledgeReadComponent component, ref UseInHandEvent args)
    {
        if (!TryComp<HereticComponent>(args.User, out var heretic))
            return;

        var knowledge =  component.KnowledgeBook;

        _heretic.UpdateKnowledge(args.User, heretic, knowledge);
        RemComp<KnowledgeReadComponent>(book);

        args.Handled = true;
    }
}
