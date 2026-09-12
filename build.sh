#!/usr/bin/env bash
# Sandbox build helper (on your Windows machine just open the .sln in Visual Studio, or: dotnet build)
set -e
export DOTNET_ROOT=/tmp/dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export NUGET_PACKAGES=/tmp/nuget
export DOTNET_CLI_HOME=/tmp/dotnet_home
cd "$(dirname "$0")"
dotnet build LastOneRich.sln -c Release "$@"
