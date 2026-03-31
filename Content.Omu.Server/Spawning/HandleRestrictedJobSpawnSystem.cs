using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;

namespace Content.Omu.Server.Spawning;

public sealed class HandleRestrictedJobSpawnSystem : EntitySystem
{
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly GameTicker _ticker = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawningEvent>(
            OnPlayerSpawning,
            before: new[]
            {
                typeof(ContainerSpawnPointSystem),
                typeof(ArrivalsSystem)
            });
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null || args.Job == null)
            return;

        EntityUid? chosen = null;
        TransformComponent? chosenXform = null;

        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var spawn, out var xform))
        {
            if (!spawn.Forced || spawn.Job != args.Job)
                continue;

            chosen = uid;
            chosenXform = xform;
            break;
        }

        if (chosen == null || chosenXform == null)
            return;

        // latejoin only
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        var coords = chosenXform.Coordinates;

        Spawn("EffectFlashBluespace", coords); // todo unhardcode.

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            coords,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!ev.LateJoin || ev.JobId == null)
            return;

        var query = EntityQueryEnumerator<SpawnPointComponent>();
        while (query.MoveNext(out _, out var spawn))
        {
            if (!spawn.Forced) // todo kinda hardcoded atm.
                continue;

            if (spawn.Job != ev.JobId)
                continue;

            _chat.DispatchServerMessage(ev.Player,
                Loc.GetString("latejoin-forced-job-spawn"));

            return;
        }
    }
}
