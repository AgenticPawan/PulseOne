#!/usr/bin/env python3
"""phase-metrics.py — token capture + self-learning trigger.

Wired to: SubagentStop. Each phase is implemented by a phase subagent, so a
subagent finishing IS the end of a phase. Reads the hook JSON from stdin, sums
the token usage recorded in that subagent's transcript, appends one line to
.claude/phase-metrics.jsonl, and asks the orchestrator to record a self-learning
memory for the phase.

Fail-open by design: any error prints nothing actionable and exits 0 so a
metrics problem can never block phase completion (per user instruction:
"If cli stucks due to this, ignore").
"""
import sys
import os
import json
import datetime


def main():
    try:
        raw = sys.stdin.read()
        payload = json.loads(raw) if raw.strip() else {}
    except Exception:
        sys.exit(0)

    phase = os.environ.get("PULSEONE_PHASE", "unknown")
    transcript_path = payload.get("transcript_path", "")

    totals = {
        "input_tokens": 0,
        "output_tokens": 0,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 0,
    }
    messages = 0

    if transcript_path and os.path.isfile(transcript_path):
        try:
            with open(transcript_path, "r", encoding="utf-8") as fh:
                for line in fh:
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        entry = json.loads(line)
                    except Exception:
                        continue
                    usage = (entry.get("message") or {}).get("usage") or entry.get("usage")
                    if not isinstance(usage, dict):
                        continue
                    messages += 1
                    for k in totals:
                        v = usage.get(k)
                        if isinstance(v, int):
                            totals[k] += v
        except Exception:
            pass

    total_all = sum(totals.values())
    record = {
        "timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "phase": phase,
        "assistant_messages": messages,
        **totals,
        "total_tokens": total_all,
    }

    project_root = os.environ.get("PULSEONE_PROJECT_ROOT", os.getcwd())
    metrics_path = os.path.join(project_root, ".claude", "phase-metrics.jsonl")
    try:
        os.makedirs(os.path.dirname(metrics_path), exist_ok=True)
        with open(metrics_path, "a", encoding="utf-8") as fh:
            fh.write(json.dumps(record) + "\n")
    except Exception:
        pass

    context = (
        f"Phase {phase} subagent finished. Token usage captured to "
        f".claude/phase-metrics.jsonl: {total_all:,} total tokens "
        f"(in={totals['input_tokens']:,}, out={totals['output_tokens']:,}, "
        f"cache_read={totals['cache_read_input_tokens']:,}). "
        "SELF-LEARNING: before continuing, write one concise auto-memory entry "
        "capturing what was non-obvious or reusable from this phase "
        "(pitfalls hit, decisions made, patterns to repeat) so later phases benefit. "
        "Skip if nothing notable."
    )

    out = {
        "systemMessage": f"Phase {phase}: {total_all:,} tokens logged to .claude/phase-metrics.jsonl",
        "hookSpecificOutput": {
            "hookEventName": "SubagentStop",
            "additionalContext": context,
        },
    }
    print(json.dumps(out))
    sys.exit(0)


if __name__ == "__main__":
    main()
