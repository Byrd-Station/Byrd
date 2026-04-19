#!/usr/bin/env bash

tmux new-session "dotnet run --project Content.Omu.Client;pause"\; \
  splitw "dotnet run --project Content.Omu.Server;pause"
