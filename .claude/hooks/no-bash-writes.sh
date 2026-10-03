#!/usr/bin/env bash
# Denies Bash commands that write files in place, so edits go through the Rider MCP tools.
# Writes to /dev/null, stream duplication (2>&1) and temp paths stay allowed.
cmd=$(jq -r '.tool_input.command // empty')
[ -z "$cmd" ] && exit 0

allowed='("?)(/dev/null|\$TMPDIR|\$\{TMPDIR\}|/tmp/|/private/tmp/)[^[:space:];|&]*'
c=$(printf '%s' "$cmd" | sed -E \
    -e 's/[0-9]*>&[0-9-]+//g' \
    -e "s#&>>?[[:space:]]*${allowed}##g" \
    -e "s#[0-9]*>>?[[:space:]]*${allowed}##g" \
    -e "s#tee([[:space:]]+-a)?[[:space:]]+${allowed}##g")
# Quoted text (commit messages, jq filters, grep patterns) is not a write.
c=$(printf '%s' "$c" | sed -E -e "s/'[^']*'//g" -e 's/"[^"]*"//g')

if printf '%s' "$c" | grep -Eq '>' \
    || printf '%s' "$c" | grep -Eq '(^|[[:space:];|&(])sed([[:space:]][^;|&]*)?[[:space:]](--in-place|-[a-zA-Z]*i)' \
    || printf '%s' "$c" | grep -Eq '(^|[[:space:];|&(])perl([[:space:]][^;|&]*)?[[:space:]]-[a-zA-Z0-9]*i' \
    || printf '%s' "$c" | grep -Eq '(^|[[:space:];|&(])tee([[:space:]]|$)'; then
    printf '%s\n' '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"This repo requires the Rider MCP for file edits: use mcp__rider__apply_patch or mcp__rider__create_new_file instead."}}'
fi
exit 0
