using Content.Shared.ActionBlocker;
using Content.Shared.Emoting;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Throwing;
using Content.Shared._Omu.Resomi.Components;

namespace Content.Shared._Omu.Resomi.EntitySystems;

/// <summary>
/// Prevents a nesting Resomi from moving or interacting with the world.
/// Speech and emotes are still allowed so they can chirp from the nest.
/// </summary>
public abstract class SharedNestingFrozenSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NestingFrozenComponent, UseAttemptEvent>(OnUseAttempt);
        SubscribeLocalEvent<NestingFrozenComponent, PickupAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<NestingFrozenComponent, ThrowAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<NestingFrozenComponent, InteractionAttemptEvent>(OnInteractAttempt);
        SubscribeLocalEvent<NestingFrozenComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NestingFrozenComponent, ComponentShutdown>(UpdateCanMove);
        SubscribeLocalEvent<NestingFrozenComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
        SubscribeLocalEvent<NestingFrozenComponent, PullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<NestingFrozenComponent, AttackAttemptEvent>(OnCancellableAttempt);
        SubscribeLocalEvent<NestingFrozenComponent, ChangeDirectionAttemptEvent>(OnCancellableAttempt);
        // Note: EmoteAttemptEvent and SpeakAttemptEvent are NOT blocked so Resomi can chirp from nest.
    }

    private void OnUseAttempt(EntityUid uid, NestingFrozenComponent component, UseAttemptEvent args)
    {
        if (!TryComp<NestingComponent>(uid, out var nestingComponent))
        {
            args.Cancel();
            return;
        }

        // Allow only the exit nest action
        if (nestingComponent.ExitNestActionEntity != null && args.Used == nestingComponent.ExitNestActionEntity)
            return;

        args.Cancel();
    }

    private void OnCancellableAttempt(EntityUid uid, NestingFrozenComponent component, CancellableEntityEventArgs args)
    {
        args.Cancel();
    }

    private void OnPullAttempt(EntityUid uid, NestingFrozenComponent component, PullAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnStartup(EntityUid uid, NestingFrozenComponent component, ComponentStartup args)
    {
        if (TryComp<PullableComponent>(uid, out var pullable))
            _pulling.TryStopPull(uid, pullable);

        UpdateCanMove(uid, component, args);
    }

    private void OnUpdateCanMove(EntityUid uid, NestingFrozenComponent component, UpdateCanMoveEvent args)
    {
        if (component.LifeStage > ComponentLifeStage.Running)
            return;

        args.Cancel();
    }

    private void UpdateCanMove(EntityUid uid, NestingFrozenComponent component, EntityEventArgs args)
    {
        _blocker.UpdateCanMove(uid);
    }

    private void OnInteractAttempt(Entity<NestingFrozenComponent> ent, ref InteractionAttemptEvent args)
    {
        if (args.Target == null)
            return;

        args.Cancelled = true;
    }
}
