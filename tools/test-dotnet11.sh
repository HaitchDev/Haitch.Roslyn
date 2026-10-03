#!/usr/bin/env bash
set -euo pipefail

# Runs on a copy so the working tree's global.json (the .NET 10 pin) is never modified.
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# pwd -P: on macOS /var is a symlink to /private/var, and MSBuild would otherwise see each project under two paths and build it twice concurrently.
work="$(cd "$(mktemp -d)" && pwd -P)"
trap 'rm -rf "$work"' EXIT

tar -C "$repo" --exclude=./bin --exclude=./obj --exclude=bin --exclude=obj --exclude=.git -cf - . | tar -C "$work" -xf -

# A preview floor lets any 11.0 preview, RC or release satisfy the pin; every other key is kept.
jq '.sdk.version = "11.0.100-preview.1" | .sdk.rollForward = "latestFeature" | .sdk.allowPrerelease = true' "$work/global.json" > "$work/global.json.tmp"
mv "$work/global.json.tmp" "$work/global.json"

cd "$work"
# The pack tests assert repository metadata, which SourceLink reads from git.
git init -q
git remote add origin "$(git -C "$repo" remote get-url origin 2>/dev/null || echo https://github.com/example/example.git)"
git add -A
git -c user.name=ci -c user.email=ci@example.com commit -q -m ci
echo "SDK version: $(dotnet --version)"
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
