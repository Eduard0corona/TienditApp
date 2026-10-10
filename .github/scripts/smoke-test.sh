#!/usr/bin/env bash
# Waits until the app answers with this version: ready (the API reaches the database), with every migration of this
# version in the database, serving the index.html just built, and its script compressed (Brotli) and cached for a year.
# App Service restarts take a few minutes, so it tries 30 times, 20 seconds apart.
#   $1: the app's address (the slot's, while it is still in staging)
#   $2: the script that index.html loads; its name carries the hash of the build, so an old instance does not pass
set -uo pipefail

url="${1%/}"
script="$2"

for attempt in $(seq 1 30); do
  ready=$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 "$url/health/ready" || true)
  # The status in the JSON, not just a 200: a version without the endpoint answers with the client's index.html.
  schema=$(curl -s --max-time 20 "$url/health/schema" | grep -oE '"status":"[A-Za-z]+"' | head -n 1 | cut -d'"' -f4 || true)
  if curl -s --max-time 20 "$url/" | grep -qF "$script"; then served=yes; else served=no; fi
  # The headers of the script as a browser asks for it (Http/ClientDelivery.cs): the publish leaves its Brotli copy.
  headers=$(curl -s -o /dev/null -D - --max-time 20 -H 'Accept-Encoding: br, gzip' "$url$script" | tr -d '\r' || true)
  if grep -qi '^content-encoding: br' <<< "$headers" && grep -qi '^cache-control:.*immutable' <<< "$headers"; then
    delivered=yes
  else
    delivered=no
  fi
  if [ "$ready" = "200" ] && [ "$schema" = "Healthy" ] && [ "$served" = "yes" ] && [ "$delivered" = "yes" ]; then
    echo "$url answers with this version: ready, with its migrations applied and serving $script compressed and cached."
    exit 0
  fi
  echo "Attempt $attempt: /health/ready=$ready, /health/schema=${schema:-no answer}, new client served=$served, compressed and cached=$delivered."
  sleep 20
done

if [ "$schema" = "Unhealthy" ]; then
  echo "::error::The database lacks migrations of this version: apply database/production/<MigrationId>/apply.sql (docs/development/database-migrations.md)."
elif [ "$served" = "yes" ] && [ "$delivered" = "no" ]; then
  echo "::error::$url serves this version, but its script is not compressed with Brotli and cached for a year: check that the publish left the .br copies in wwwroot/assets (CompressionEnabled in the WebApi csproj)."
else
  echo "::error::$url did not answer with this version within 10 minutes (readiness, schema or index.html). Check the App Service log stream."
fi
exit 1
