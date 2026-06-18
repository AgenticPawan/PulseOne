#!/usr/bin/env bash
# post-generate hook — lint, format, build, test after code generation
# Wired to: all phase agents, runs after each component is generated
set -euo pipefail

PHASE="${1:-all}"

echo "=== PulseOne Post-Generate Hook (phase: $PHASE) ==="

run_backend_checks() {
  echo "--- Backend checks ---"

  # Format check
  if command -v dotnet-format &> /dev/null; then
    echo "Running dotnet format..."
    dotnet format src/backend/PulseOne.sln --verify-no-changes --severity warn
  fi

  # Build (incremental — this hook fires after every Write, so a clean rebuild each time is
  # wasteful; incremental keeps repeated post-write checks cheap). The full test suite + code
  # coverage is intentionally NOT run here; it runs once at phase end via phase-complete.sh.
  echo "Building solution..."
  dotnet build src/backend/PulseOne.sln --configuration Release 2>&1 | tail -20
}

run_angular_checks() {
  local APP_DIR="${1}"
  local APP_NAME="${2}"
  echo "--- Angular checks: $APP_NAME ---"

  if [ -d "$APP_DIR" ]; then
    cd "$APP_DIR"

    echo "Running ESLint..."
    npx ng lint --max-warnings=0 || echo "WARNING: ESLint issues found"

    echo "Running Angular build..."
    npx ng build --configuration=production 2>&1 | tail -20

    cd -
  else
    echo "Skipping $APP_NAME (directory not yet created)"
  fi
}

run_secret_scan() {
  echo "--- Secret scan ---"
  if command -v gitleaks &> /dev/null; then
    gitleaks detect --source=src/ --no-git --redact && echo "PASS: No secrets detected" || {
      echo "ERROR: Secret detected after generation. Remove it immediately."
      exit 1
    }
  fi
}

# Run checks based on phase
case "$PHASE" in
  "0"|"foundation")
    run_backend_checks
    ;;
  "1"|"auth")
    run_backend_checks
    run_angular_checks "src/client-app" "client-app"
    run_angular_checks "src/host-admin-app" "host-admin-app"
    ;;
  "2"|"core-backend")
    run_backend_checks
    ;;
  "3"|"background-jobs")
    run_backend_checks
    ;;
  "4"|"payment")
    run_backend_checks
    run_angular_checks "src/client-app" "client-app"
    run_secret_scan
    ;;
  "5"|"host-portal")
    run_angular_checks "src/host-admin-app" "host-admin-app"
    ;;
  "6"|"tenant-portal")
    run_angular_checks "src/client-app" "client-app"
    ;;
  "7"|"testing")
    run_backend_checks
    if [ -d "e2e-tests" ]; then
      echo "Running Playwright tests..."
      cd e2e-tests && npx playwright test --reporter=line
    fi
    ;;
  "all"|*)
    run_backend_checks
    run_angular_checks "src/client-app" "client-app"
    run_angular_checks "src/host-admin-app" "host-admin-app"
    run_secret_scan
    ;;
esac

echo "=== Post-generate hook complete ==="
