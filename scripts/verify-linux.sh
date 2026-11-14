#!/usr/bin/env bash
# Run this inside an existing Linux checkout with the SDK from global.json installed.
# Keep Linux build output separate from a Windows build of the same source tree.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts/platform/linux
dotnet --info | tee artifacts/platform/linux/dotnet-info.txt
uname -a | tee artifacts/platform/linux/kernel.txt
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test tests/LibraryLending.Tests.Unit --no-build --logger 'trx;LogFileName=linux-unit.trx' --results-directory artifacts/platform/linux/unit
dotnet test tests/LibraryLending.Tests.Integration --no-build --logger 'trx;LogFileName=linux-integration.trx' --results-directory artifacts/platform/linux/integration
bash scripts/smoke-linux.sh
