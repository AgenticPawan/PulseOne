#!/usr/bin/env bash
# on-error hook — rollback, alert, and diagnostic capture on agent failure
# Wired to: all phase agents, fires when any agent reports failure
set -euo pipefail

PHASE="${1:-unknown}"
ERROR_MSG="${2:-Unknown error}"
TIMESTAMP=$(date -u +"%Y%m%dT%H%M%SZ")
LOG_DIR=".claude/error-logs"

mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/error-${PHASE}-${TIMESTAMP}.log"

echo "=== PulseOne On-Error Hook ==="
echo "Phase: $PHASE"
echo "Error: $ERROR_MSG"
echo "Timestamp: $TIMESTAMP"
echo ""

# Capture diagnostic information
{
  echo "=== ERROR REPORT ==="
  echo "Phase: $PHASE"
  echo "Error: $ERROR_MSG"
  echo "Timestamp: $TIMESTAMP"
  echo ""
  echo "=== Git Status ==="
  git status 2>/dev/null || echo "Not a git repo"
  echo ""
  echo "=== Recent Changes ==="
  git diff --stat HEAD 2>/dev/null || echo "No git history"
  echo ""
  echo "=== Build Errors ==="
  dotnet build src/backend/PulseOne.sln 2>&1 | grep -E "error|Error" | head -20 || true
  echo ""
  echo "=== Test Failures ==="
  find TestResults/ -name "*.trx" -newer "$LOG_FILE" -exec cat {} \; 2>/dev/null | grep -A5 "Failed\|Error" | head -50 || true
} > "$LOG_FILE"

echo "Diagnostic log written to: $LOG_FILE"

# Check if git is available and there are staged changes to unstage
if git rev-parse --git-dir &>/dev/null 2>&1; then
  STAGED=$(git diff --cached --name-only 2>/dev/null | wc -l)
  if [ "$STAGED" -gt 0 ]; then
    echo ""
    echo "WARNING: $STAGED file(s) are staged. Review before committing:"
    git diff --cached --name-only
    echo ""
    echo "To unstage all: git reset HEAD"
    echo "To discard changes: git checkout -- <file>"
  fi
fi

echo ""
echo "Recovery suggestions:"
echo "  1. Review $LOG_FILE for error details"
echo "  2. Fix the issue and re-run the phase agent"
echo "  3. If a file was partially written, read it and check for syntax errors"
echo "  4. Check .claude/prompts/phase-${PHASE}/ for the implementation spec"

exit 0  # Don't cascade — the original error is already known
