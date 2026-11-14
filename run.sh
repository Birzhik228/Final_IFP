#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if command -v dotnet >/dev/null 2>&1 && dotnet --version 2>/dev/null | grep -q '^8\.0\.'; then
  sdk="$(command -v dotnet)"
else
  mkdir -p .tools/dotnet
  if [[ ! -x .tools/dotnet/dotnet ]]; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o .tools/dotnet-install.sh
    bash .tools/dotnet-install.sh --version 8.0.428 --install-dir "$PWD/.tools/dotnet" --no-path
  fi
  sdk="$PWD/.tools/dotnet/dotnet"
  export DOTNET_ROOT="$PWD/.tools/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://localhost:${PORT:-5180}"
printf 'BookBridge: %s\nLocal demo: librarian@library.local / LibraryDemo1!\n' "$ASPNETCORE_URLS"
exec "$sdk" run --project src/LibraryLending.Web --no-launch-profile
