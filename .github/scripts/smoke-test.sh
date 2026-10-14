#!/usr/bin/env bash
# Waits until the app answers with this version: ready (the API reaches the database), with the time zones a business
# can pick resolving on this host, with every migration of this version in the database, serving the index.html just
# built, and its script compressed (Brotli) and cached for a year.
# The health endpoints name the commit the API was built from, so an instance of the previous version does not pass,
# not even when the client did not change and its script kept its name. App Service restarts take a few minutes, so it
# tries 30 times, 20 seconds apart. Without an email provider it passes with a warning: the app works, but nobody who
# signs up can activate their account.
#   $1: the app's address (the slot's, while it is still in staging)
#   $2: the script that index.html loads; its name carries the hash of the client build
#   $3: the commit deployed (github.sha), which /health/ready and /health/schema must name
set -uo pipefail

url="${1%/}"
script="$2"
commit="${3:?the deployed commit (github.sha) is the third argument}"

# The commit a health answer names, or nothing (a version before the field, or not an answer of the API).
commit_of() {
  grep -oE '"commit":"[0-9a-f]+"' <<< "$1" | head -n 1 | cut -d'"' -f4 || true
}

for attempt in $(seq 1 30); do
  # The body and, on its last line, the status code: each instance answers for itself, so both checks look at the
  # commit of the answer they read.
  ready_answer=$(curl -s -w '\n%{http_code}' --max-time 20 "$url/health/ready" || true)
  ready="${ready_answer##*$'\n'}"
  # A time zone that falls back to UTC leaves readiness Degraded, which still answers 200: the entry itself is read
  # (BusinessTimeZonesHealthCheck).
  zones=$(grep -oE '"business-time-zones":"[A-Za-z]+"' <<< "$ready_answer" | head -n 1 | cut -d'"' -f4 || true)
  # The same for the email provider (EmailProviderHealthCheck), which only warns.
  email=$(grep -oE '"email-provider":"[A-Za-z]+"' <<< "$ready_answer" | head -n 1 | cut -d'"' -f4 || true)
  schema_answer=$(curl -s --max-time 20 "$url/health/schema" || true)
  # The status in the JSON, not just a 200: a version without the endpoint answers with the client's index.html.
  schema=$(grep -oE '"status":"[A-Za-z]+"' <<< "$schema_answer" | head -n 1 | cut -d'"' -f4 || true)
  answered=$(commit_of "$schema_answer")
  if [ "$(commit_of "$ready_answer")" = "$commit" ] && [ "$answered" = "$commit" ]; then current=yes; else current=no; fi
  if curl -s --max-time 20 "$url/" | grep -qF "$script"; then served=yes; else served=no; fi
  # The headers of the script as a browser asks for it (Http/ClientDelivery.cs): the publish leaves its Brotli copy.
  headers=$(curl -s -o /dev/null -D - --max-time 20 -H 'Accept-Encoding: br, gzip' "$url$script" | tr -d '\r' || true)
  if grep -qi '^content-encoding: br' <<< "$headers" && grep -qi '^cache-control:.*immutable' <<< "$headers"; then
    delivered=yes
  else
    delivered=no
  fi
  if [ "$current" = "yes" ] && [ "$ready" = "200" ] && [ "$zones" = "Healthy" ] && [ "$schema" = "Healthy" ] \
    && [ "$served" = "yes" ] && [ "$delivered" = "yes" ]; then
    echo "$url answers with this version ($commit): ready, with its time zones and migrations, and serving $script compressed and cached."
    if [ "$email" != "Healthy" ]; then
      echo "::warning::$url has no email provider configured (email-provider=${email:-no answer}): nobody who signs up can activate their account, and password resets and team invitations by email are not sent. Configure EmailSettings (docs/development/deploy-azure.md); the log of the email-provider check says what is missing."
    fi
    exit 0
  fi
  echo "Attempt $attempt: commit=${answered:-none}, /health/ready=$ready, time zones=${zones:-no answer}, /health/schema=${schema:-no answer}, new client served=$served, compressed and cached=$delivered."
  sleep 20
done

# Only this version's own answer says what it lacks: the previous one does not know the new migrations.
if [ -n "$answered" ] && [ "$answered" != "$commit" ]; then
  echo "::error::$url still answers with commit $answered, not $commit: this version did not start within 10 minutes. Check the App Service log stream."
elif [ "$current" = "yes" ] && [ "$schema" = "Unhealthy" ]; then
  echo "::error::The database lacks migrations of this version: apply database/production/<MigrationId>/apply.sql (docs/development/database-migrations.md)."
elif [ "$current" = "yes" ] && [ "$zones" = "Degraded" ]; then
  echo "::error::A time zone a business can pick resolves to UTC on $url: the host lacks its time-zone data, so the hours and dates of those businesses would run in UTC. The log of the business-time-zones check names them (docs/architecture/observability.md)."
elif [ "$current" = "yes" ] && [ "$served" = "yes" ] && [ "$delivered" = "no" ]; then
  echo "::error::$url serves this version, but its script is not compressed with Brotli and cached for a year: check that the publish left the .br copies in wwwroot/assets (CompressionEnabled in the WebApi csproj)."
else
  echo "::error::$url did not answer with this version within 10 minutes (commit, readiness, schema or index.html). Check the App Service log stream."
fi
exit 1
