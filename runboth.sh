#!/usr/bin/env bash

# Otherwise it'll be very mad
dotnet build Content.Shared --configuration Debug || exit

# Run server and client side-by-side in tmux
tmux new-session "DRI_PRIME=1 dotnet run --project Content.Omu.Client;pause"\; \
  splitw "dotnet run --project Content.Omu.Server;pause"
