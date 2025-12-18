using Content.Omu.Server.HereticalTome.Components;
using Content.Shared.Examine;

namespace Content.Omu.Server.HereticalTome;

public sealed class FascinationSystem: EntitySystem
{
public override void Initialize()
{
    base.Initialize();

    SubscribeLocalEvent<FascinationComponent, ExaminedEvent>(OnExamined);
}

    private void OnExamined(Entity<FascinationComponent> ent, ref ExaminedEvent args)
    {
        var comp = ent.Comp;
        comp.FascinationDesc = comp.FascinationInt switch
        {
            0 => Loc.GetString("Fascination-0"),
            1 => Loc.GetString("Fascination-1"),
            2 => Loc.GetString("Fascination-2"),
            3 => Loc.GetString("Fascination-3"),
            4 => Loc.GetString("Fascination-4"),
            5 => Loc.GetString("Fascination-5"),
            _ => comp.FascinationDesc
        };
        args.PushMarkup(comp.FascinationDesc ?? Loc.GetString("Fascination-0"));
        Dirty(ent);
    }
}
