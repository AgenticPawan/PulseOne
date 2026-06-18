#!/usr/bin/env bash
# phase-complete.sh — end-of-phase verification.
# Wired to: SubagentStop. Each phase is implemented by a phase subagent, so a subagent finishing
# IS the end of a phase. This runs the EXPENSIVE pass — full test suite + code coverage — exactly
# once per phase, instead of on every file Write (which is what post-generate.sh used to do).
set -euo pipefail

PHASE="${1:-all}"

echo "=== PulseOne Phase-Complete Hook (phase: $PHASE) ==="

# Angular-only phases ship no .NET tests — skip the backend suite entirely.
case "$PHASE" in
  "5"|"host-portal"|"6"|"tenant-portal")
    echo "Phase $PHASE is Angular-only — no .NET test suite to run."
    echo "=== Phase-complete hook done ==="
    exit 0
    ;;
esac

echo "Running full test suite with code coverage (once for this phase)..."
dotnet test src/backend/PulseOne.sln \
  --configuration Release \
  --logger "trx" \
  --results-directory TestResults/ \
  --collect "XPlat Code Coverage" \
  2>&1 | tail -30

echo "=== Phase-complete hook done ==="
