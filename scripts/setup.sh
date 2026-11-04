#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
command -v dotnet >/dev/null || { echo 'Zainstaluj .NET SDK 10.'; exit 1; }
command -v node >/dev/null || { echo 'Zainstaluj Node.js 22.'; exit 1; }
command -v docker >/dev/null || { echo 'Zainstaluj i uruchom Docker Desktop.'; exit 1; }
dotnet tool restore
dotnet restore Qraft.slnx --locked-mode
npm ci --prefix apps/web
dotnet build Qraft.slnx --no-restore -m:1
mkdir -p data .dev/logs
echo 'Gotowe. Uruchom: npm run dev'
