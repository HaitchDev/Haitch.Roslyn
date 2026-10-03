#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

dotnet tool restore
dotnet restore
dotnet build --no-restore
dotnet csharpier check .
dotnet test --no-build
