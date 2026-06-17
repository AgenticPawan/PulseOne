#!/usr/bin/env bash
# pre-generate hook — validates design documents before code generation
# Wired to: all phase agents, runs before any code is written
set -euo pipefail

BLUEPRINT="docs/PulseOne-Blueprint-v2.md"

echo "=== PulseOne Pre-Generate Hook ==="

# 1. Blueprint must exist
if [ ! -f "$BLUEPRINT" ]; then
  echo "ERROR: Blueprint not found at $BLUEPRINT"
  exit 1
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
if grep -rn '"default"' src/backend/ --include="*.cs" | grep -i tenant | grep -v "//"; then
  echo "WARNING: Possible tenant default found. Verify it's not a TenantContext default."
fi

# b. Non-constant-time comparison
if grep -rn '!=' src/backend/ --include="*.cs" | grep -i signature | grep -v "//"; then
  echo "WARNING: Possible non-constant-time signature comparison. Use CryptographicOperations.FixedTimeEquals."
fi

# c. Hardcoded Razorpay key
if grep -rn 'rzp_test_\|rzp_live_' src/ 2>/dev/null; then
  echo "ERROR: Hardcoded Razorpay key detected. Keys must come from Key Vault."
  exit 1
fi

echo "Pre-generate checks passed."
