#!/usr/bin/env bash
# pre-generate hook — validates design documents before code generation
# Wired to: all phase agents, runs before any code is written
set -euo pipefail

BLUEPRINT="docs/PulseOne-Blueprint-v2.md"

echo "=== PulseOne Pre-Generate Hook ==="

# 1. Blueprint is the design source of truth. Warn (don't block) if it's absent — a missing
# blueprint must not hard-fail every Write. The secret/anti-pattern gates below still run.
if [ ! -f "$BLUEPRINT" ]; then
  echo "WARNING: Blueprint not found at $BLUEPRINT — proceeding without design-doc validation."
fi

# 2. Secret scan on existing source (before generating more)
if command -v gitleaks &> /dev/null; then
  echo "Running gitleaks secret scan..."
  gitleaks detect --source=src/ --no-git --redact || {
    echo "ERROR: Secrets detected in source. Fix before generating new code."
    exit 1
  }
else
  echo "WARNING: gitleaks not installed. Skipping secret scan."
  echo "Install: https://github.com/zricethezav/gitleaks"
fi

# 3. Check for v1 anti-patterns in existing code
echo "Checking for v1 anti-patterns..."

# a. Tenant context defaulting to "default"
# Exclude bin/obj — after a build they fill with generated .cs files that don't need scanning.
if grep -rn '"default"' src/backend/ --include="*.cs" --exclude-dir={bin,obj} | grep -i tenant | grep -v "//"; then
  echo "WARNING: Possible tenant default found. Verify it's not a TenantContext default."
fi

# b. Non-constant-time comparison
if grep -rn '!=' src/backend/ --include="*.cs" --exclude-dir={bin,obj} | grep -i signature | grep -v "//"; then
  echo "WARNING: Possible non-constant-time signature comparison. Use CryptographicOperations.FixedTimeEquals."
fi

# c. Hardcoded Razorpay key
# Scope the scan: never recurse into dependency/build/VCS trees. Recursing into node_modules
# in particular caused multi-minute stalls. -l stops at the first match per file (we only need
# to know IF a key exists, not every line).
if grep -rln --exclude-dir={node_modules,dist,bin,obj,.git} 'rzp_test_\|rzp_live_' src/ 2>/dev/null; then
  echo "ERROR: Hardcoded Razorpay key detected. Keys must come from Key Vault."
  exit 1
fi

echo "Pre-generate checks passed."
