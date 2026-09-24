#!/bin/sh
# Compiles the WinUI app's C# code on Linux using XAML stubs (no XAML markup validation).
# XAML itself is validated by the Windows CI build.
set -e
DIR="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="$PATH:/root/.dotnet"
python3 "$DIR/scripts/xaml-stubgen.py" "$DIR/src/Amperfy.App" "$DIR/src/Amperfy.App/obj/xamlstubs"
dotnet build "$DIR/src/Amperfy.App/Amperfy.App.csproj" -p:AmperfyLinuxCheck=true -p:EnableWindowsTargeting=true -p:Platform=x64 -r win-x64 -nologo -v:q "$@" 2>&1 | grep -E "error|warning CS|Build succeeded|Time Elapsed" | sort -u
