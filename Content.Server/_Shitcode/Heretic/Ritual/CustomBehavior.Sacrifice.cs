// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <aiden@djkraz.com>
// SPDX-FileCopyrightText: 2025 Aviu00 <93730715+Aviu00@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Ilya246 <ilyukarno@gmail.com>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
// SPDX-FileCopyrightText: 2025 username <113782077+whateverusername0@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 whateverusername0 <whateveremail>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server._Goobstation.Objectives.Components;
using Content.Server.Body.Systems;
using Content.Server.Heretic.Components;
using Content.Shared.Heretic.Prototypes;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Humanoid;
using Content.Server.Revolutionary.Components;
using Content.Shared.Mind;
using Content.Shared.Heretic;
using Content.Server.Heretic.EntitySystems;
using Content.Shared.Gibbing.Events;
//impstation start
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Server.Humanoid;
using Content.Shared.Forensics.Components;
using Robust.Shared.Toolshed.TypeParsers;
using Robust.Server.GameObjects;
using System;
using System.Linq;
using Content.Server._Goobstation.Heretic.EntitySystems;
using Content.Server.Heretic.Components;
using Content.Server.Forensics;
using Content.Server.Body.Systems;
using Content.Server.Body.Components;
using Content.Shared.Forensics;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameObjects;
using Content.Shared.Chemistry.EntitySystems;
using Content.Server._Imp.Heretic.Components;
using Content.Shared.Body.Components;
//impstation end
using Content.Shared.Silicons.Borgs.Components;


namespace Content.Server.Heretic.Ritual;

/// <summary>
///     Checks for a nearest dead body,
///     gibs it and gives the heretic knowledge points.
/// </summary>
// these classes should be lead out and shot
[Virtual] public partial class RitualSacrificeBehavior : RitualCustomBehavior
{
    /// <summary>
    ///     Minimal amount of corpses.
    /// </summary>
    [DataField]
    public float Min = 1;

    /// <summary>
    ///     Maximum amount of corpses.
    /// </summary>
    [DataField]
    public float Max = 1;

    /// <summary>
    ///     Should we count only targets?
    /// </summary>
    [DataField]
    public bool OnlyTargets;

    /// <summary>
    ///     Should we count only humanoids?
    /// </summary>
    [DataField]
    public bool OnlyHumanoid = true;

    // this is awful but it works so i'm not complaining
    // i'm complaining -kandiyaki //IMP
    // i, too am in this episode -dooty //OMU
    protected SharedMindSystem _mind = default!;
    protected HereticSystem _heretic = default!;
    protected SharedTransformSystem _xform = default!; //imp
    protected DamageableSystem _damage = default!; //imp
    protected BodySystem _body = default!;
    protected EntityLookupSystem _lookup = default!;
    protected HumanoidAppearanceSystem _humanoid = default!; //imp
    protected TransformSystem _transformSystem = default!; //imp
    protected HellWorldSystem _hellworld = default!; //imp
    protected BloodstreamSystem _bloodstream = default!; //imp
    protected SharedSolutionContainerSystem _solutionContainerSystem = default!; //imp


    [Dependency] protected IPrototypeManager _proto = default!;
    [Dependency] protected ILogManager _log = default!;

    private ISawmill? _sawmill;

    protected List<EntityUid> uids = new();

    public override bool Execute(RitualData args, out string? outstr)
    {
        //it was like this when i got here -kandiyaki //imp
        _mind = args.EntityManager.System<SharedMindSystem>();
        _heretic = args.EntityManager.System<HereticSystem>();
        _xform = args.EntityManager.System<SharedTransformSystem>(); //imp
        _damage = args.EntityManager.System<DamageableSystem>(); //imp
        _body = args.EntityManager.System<BodySystem>();
        _lookup = args.EntityManager.System<EntityLookupSystem>();
        _humanoid = args.EntityManager.System<HumanoidAppearanceSystem>(); //imp
        _transformSystem = args.EntityManager.System<TransformSystem>(); //imp
        _hellworld = args.EntityManager.System<HellWorldSystem>(); //imp
        _bloodstream = args.EntityManager.System<BloodstreamSystem>(); //imp
        _solutionContainerSystem = args.EntityManager.System<SharedSolutionContainerSystem>(); //imp

        _proto = IoCManager.Resolve<IPrototypeManager>();
        _log = IoCManager.Resolve<ILogManager>();

        uids = new();

        if (!args.EntityManager.TryGetComponent<HereticComponent>(args.Performer, out var hereticComp))
        {
            outstr = string.Empty;
            return false;
        }

        var lookup = _lookup.GetEntitiesInRange(args.Platform, 1.5f);
        if (lookup.Count == 0)
        {
            outstr = Loc.GetString("heretic-ritual-fail-sacrifice");
            return false;
        }

        // get all the dead ones
        foreach (var look in lookup)
        {
            if (!args.EntityManager.TryGetComponent<MobStateComponent>(look, out var mobstate) // only mobs
            || OnlyHumanoid && !args.EntityManager.HasComponent<HumanoidAppearanceComponent>(look) // only humans
            || args.EntityManager.HasComponent<BorgChassisComponent>(look) // no borgs
            || !args.EntityManager.HasComponent<HumanoidAppearanceComponent>(look) // only humans
            || args.EntityManager.HasComponent<HellVictimComponent>(look) //no reusing corpses // imp
            || OnlyTargets
                && hereticComp.SacrificeTargets.All(x => x.Entity != args.EntityManager.GetNetEntity(look)) // only targets
                && !args.EntityManager.HasComponent<HereticComponent>(look)) // or other heretics
                continue;

            if (mobstate.CurrentState != Shared.Mobs.MobState.Alive)
                uids.Add(look);
        }

        if (uids.Count < Min)
        {
            outstr = Loc.GetString("heretic-ritual-fail-sacrifice-ineligible");
            return false;
        }

        outstr = null;
        return true;
    }

    //this does way too much //IMP
    public override void Finalize(RitualData args)
    {
        if (!args.EntityManager.TryGetComponent(args.Performer, out HereticComponent? heretic))
        {
            uids = new();
            return;
        }

        var knowledgeGain = 0f;
        for (var i = 0; i < Max && i < uids.Count; i++)
        {
            if (!args.EntityManager.EntityExists(uids[i]))
                continue;

            var uid = uids[i];

            var isCommand = args.EntityManager.HasComponent<CommandStaffComponent>(uid);
            var isSec = args.EntityManager.HasComponent<SecurityStaffComponent>(uid);
            var isHeretic = args.EntityManager.HasComponent<HereticComponent>(uid);
            knowledgeGain +=
                isHeretic ||
                heretic.SacrificeTargets.Any(x => x.Entity == args.EntityManager.GetNetEntity(uid))
                    ? isCommand || isSec || isHeretic ? 3f : 2f
                    : 0f;

            // YES!!! GIB!!! // actually, scratch that - dooty //OMU
            // _body.GibBody(uids[i], contents: GibContentsOption.Gib);

            //impstation start
            //get the humanoid appearance component
             if (!args.EntityManager.TryGetComponent<HumanoidAppearanceComponent>(uids[i], out var humanoid))
                 return;

            //get the species prototype from that
            if (!_proto.TryIndex(humanoid.Species, out var speciesPrototype))
                return;

            //spawn a clone of the victim
            //this should really use the cloningsystem but i coded this before that existed
            //and it works so i'm not changing it unless it causes issues
            var sacrificialWhiteBoy = args.EntityManager.Spawn(speciesPrototype.Prototype, _transformSystem.GetMapCoordinates(uids[i]));
            _humanoid.CloneAppearance(uids[i], sacrificialWhiteBoy);
            //make sure it has the right DNA
            if (args.EntityManager.TryGetComponent<DnaComponent>(uids[i], out var victimDna))
            {
                if (args.EntityManager.TryGetComponent<BloodstreamComponent>(sacrificialWhiteBoy, out var dummyBlood))
                {
                    //this is copied from BloodstreamSystem's OnDnaGenerated
                    //i hate it
                    if(_solutionContainerSystem.ResolveSolution(sacrificialWhiteBoy, dummyBlood.BloodSolutionName, ref dummyBlood.BloodSolution, out var bloodSolution))
                    {
                        foreach (var reagent in bloodSolution.Contents)
                        {
                            List<ReagentData> reagentData = reagent.Reagent.EnsureReagentData();
                            reagentData.RemoveAll(x => x is DnaData);
                            reagentData.AddRange(_bloodstream.GetEntityBloodData(uids[i]));
                        }
                    }
                }
            }
            try
            {
                _body.GibBody(sacrificialWhiteBoy, contents: GibContentsOption.Gib); // gib now - dooty
            }
            catch (Exception e)
            {
                if (!args.EntityManager.IsQueuedForDeletion(sacrificialWhiteBoy) && !args.EntityManager.Deleted(sacrificialWhiteBoy))
                    args.EntityManager.QueueDeleteEntity(sacrificialWhiteBoy);

                _sawmill ??= _log.GetSawmill("sacrifice");
                _sawmill.Error(e.Message);
            }

            //send the target to hell world
            _hellworld.AddVictimComponent(uids[i]);

            //teleport the body to a midround antag spawn spot so it's not just tossed into space
            _hellworld.TeleportRandomly(args, uids[i]);

            //make sure that my shitty AddVictimComponent thing actually worked before trying to use a mind that isn't there
            if (args.EntityManager.TryGetComponent<HellVictimComponent>(uids[i], out var hellVictim))
            {
                //i'm so sorry to all of my computer science professors. i've failed you
                if (hellVictim.HasMind)
                {
                    _hellworld.SendToHell(uids[i], args, speciesPrototype);
                }

            }
            _hellworld.SendToHell(uids[i], args, speciesPrototype);
            //impstation end

            // update objectives
            if (_mind.TryGetMind(args.Performer, out var mindId, out var mind))
            {
                // this is godawful dogshit. but it works :)
                if (_mind.TryFindObjective((mindId, mind), "HereticSacrificeObjective", out var crewObj)
                && args.EntityManager.TryGetComponent<HereticSacrificeConditionComponent>(crewObj, out var crewObjComp))
                    crewObjComp.Sacrificed += 1;

                if (_mind.TryFindObjective((mindId, mind), "HereticSacrificeHeadObjective", out var crewHeadObj)
                && args.EntityManager.TryGetComponent<HereticSacrificeConditionComponent>(crewHeadObj, out var crewHeadObjComp)
                && isCommand)
                    crewHeadObjComp.Sacrificed += 1;
            }
        }

        if (knowledgeGain > 0)
            _heretic.UpdateKnowledge(args.Performer, heretic, knowledgeGain);

        // reset it because it refuses to work otherwise.
        uids = new();
        args.EntityManager.EventBus.RaiseLocalEvent(args.Performer, new EventHereticUpdateTargets());
    }

    protected (bool isCommand, bool isSec) IsCommandOrSec(EntityUid uid, IEntityManager entityManager)
    {
        return (entityManager.HasComponent<CommandStaffComponent>(uid),
            entityManager.HasComponent<SecurityStaffComponent>(uid));
    }
}
