#!/usr/bin/env bash
# Requires a Debug build, curl, and a free port (default 5185).
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts/platform/linux
run_directory=$(mktemp -d "$PWD/artifacts/platform/linux/server.XXXXXX")
base_url="http://127.0.0.1:${BOOKBRIDGE_SMOKE_PORT:-5185}"
export ASPNETCORE_ENVIRONMENT=Development
export Demo__Seed=true
export BackgroundJobs__Enabled=false
export ConnectionStrings__Library="Data Source=$run_directory/library.db;Default Timeout=5"
dotnet src/LibraryLending.Web/bin/Debug/net8.0/LibraryLending.Web.dll --contentRoot "$PWD/src/LibraryLending.Web" --urls "$base_url" > "$run_directory/server.log" 2>&1 &
app_pid=$!
trap 'kill "$app_pid" 2>/dev/null || true; wait "$app_pid" 2>/dev/null || true' EXIT
ready=false
for attempt in {1..60}; do
    if ! kill -0 "$app_pid" 2>/dev/null; then cat "$run_directory/server.log"; exit 1; fi
    if curl --fail --silent "$base_url/health" > "$run_directory/health.json"; then ready=true; break; fi
    sleep 1
done
if [[ "$ready" != true ]]; then cat "$run_directory/server.log"; exit 1; fi
curl --fail --silent "$base_url/health/ready" > "$run_directory/ready.txt"
curl --fail --silent "$base_url/account/login" > "$run_directory/login.html"
grep -q 'BookBridge' "$run_directory/login.html"
grep -q 'Library Lending &amp; Reservations' "$run_directory/login.html"
printf '%s\n' "$run_directory" > artifacts/platform/linux/latest-smoke.txt
printf 'Linux Kestrel health, database readiness, and BookBridge login: PASS\nEvidence: %s\n' "$run_directory"
