#!/usr/bin/env bash
# Runs the backend tests. On Windows machines with Smart App Control enabled, freshly compiled unsigned DLLs are
# sometimes blocked ("An Application Control policy has blocked this file", 0x800711C7). Rebuilding with a different
# informational version changes the file hash, which is usually enough; this script retries a few times.
set -uo pipefail
cd "$(dirname "$0")/.."

for attempt in 1 2 3 4 5; do
  dotnet build -p:InformationalVersion="1.0.0+build$RANDOM$attempt" -v q -nologo >/dev/null || { dotnet build -v q; exit 1; }
  output=$(dotnet test --no-build "$@" 2>&1)
  status=$?
  if grep -q "0x800711C7" <<<"$output"; then
    echo "Attempt $attempt: a DLL was blocked by Application Control; rebuilding with a new hash..." >&2
    continue
  fi
  echo "$output"
  exit $status
done

echo "Gave up after 5 attempts blocked by Application Control." >&2
exit 1
