using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;

namespace Content.Server.CartridgeLoader.Cartridges;

public sealed class CommandMeetingsSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CommandMeetingsComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<CommandMeetingsComponent, CartridgeUiReadyEvent>(OnUiReady);
    }

    private void OnUiMessage(EntityUid uid, CommandMeetingsComponent component, CartridgeMessageEvent args)
    {
        // Currently this program does nothing, just update the UI when messages are received.
        UpdateUiState(uid, GetEntity(args.LoaderUid), component);
    }

    private void OnUiReady(EntityUid uid, CommandMeetingsComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, component);
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, CommandMeetingsComponent? component = null)
    {
        // Send an empty UI state for now.
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, new CommandMeetingsUiState());
    }
}
