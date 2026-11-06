#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
echo "EduOS developer environment: checking required tools"
dotnet --version
node --version
npm --version
if ! dotnet --list-sdks | grep -Eq '^10\.'; then
  echo "ERROR: EduOS requires .NET SDK 10.x" >&2
  exit 1
fi
if ! node --version | grep -Eq '^v22\.'; then
  echo "ERROR: EduOS frontend tooling requires Node.js 22.x" >&2
  exit 1
fi
if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: Docker CLI missing. Rebuild the dev container with docker-in-docker feature." >&2
  exit 1
fi
# Keep the EF CLI version pinned without a repository-local tool manifest.
# Existing CI and SQL workflows install the same global 10.0.12 version.
export PATH="$HOME/.dotnet/tools:$PATH"
if ! dotnet ef --version 2>/dev/null | grep -q '10.0.12'; then
  if dotnet tool list --global | grep -q '^dotnet-ef '; then
    dotnet tool update --global dotnet-ef --version 10.0.12
  else
    dotnet tool install --global dotnet-ef --version 10.0.12
  fi
fi
dotnet ef --version
dotnet restore EduOS.slnx
echo "EduOS ready: dotnet build EduOS.slnx -c Release --no-restore"
echo "For disposable local SQL Server: bash .devcontainer/sqlserver.sh start"
echo "For a connection-scoped migration: bash .devcontainer/sqlserver.sh migrate"
