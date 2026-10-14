#!/usr/bin/env bash
# Applies this version's migrations to the database of MIGRATIONS_CONNECTION, the way the deploy migrates the staging
# copy and production: `dotnet ef database update` over the API built in Release (dotnet-ef installed), as the
# Production environment. Outside Development the API host needs a JWT secret to start, so it gets a random one that
# nothing uses: the host only builds the DbContext and serves no request. Prints the migrations it applies and writes
# them to the job summary, and fails if any is left pending.
#   $1: what the database is, for the log and the summary
#   MIGRATIONS_CONNECTION: its connection string (on Azure, `Authentication=Active Directory Default` after az login)
set -euo pipefail

label="${1:?what the database is (for the log and the summary)}"
: "${MIGRATIONS_CONNECTION:?the connection string of the database to migrate}"

export ASPNETCORE_ENVIRONMENT=Production
JwtSettings__Secret="$(openssl rand -base64 48)"
export JwtSettings__Secret

ef=(
  --project src/FoodParkDigital.Infrastructure
  --startup-project src/FoodParkDigital.WebApi
  --configuration Release
  --no-build
  --connection "$MIGRATIONS_CONNECTION"
)

summary() {
  if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    echo "$1" >> "$GITHUB_STEP_SUMMARY"
  fi
}

# The migrations of this version the database lacks, one per line. On failure, what dotnet ef said. When it cannot
# reach the database, dotnet ef still lists the migrations and exits with 0, with `applied` unknown (null): that is a
# failure too, not every migration pending.
pending() {
  local output json
  if output=$(dotnet ef migrations list --json --prefix-output "${ef[@]}" 2>&1); then
    json=$(sed -n 's/^data: *//p' <<< "$output")
    if jq -e 'all(.[]; .applied | type == "boolean")' <<< "$json" > /dev/null 2>&1; then
      jq -r '.[] | select(.applied == false) | .id' <<< "$json"
      return 0
    fi
  fi
  grep -v '^data:' <<< "$output" >&2 || true
  return 1
}

# The first connection may come before a new firewall rule takes effect (it can take a few minutes on Azure SQL).
for attempt in 1 2 3 4 5 6; do
  if before=$(pending); then
    break
  fi
  if [ "$attempt" = 6 ]; then
    echo "::error::Could not read the migrations of $label."
    exit 1
  fi
  echo "Could not read the migrations of $label (attempt $attempt of 6); trying again in 30 seconds."
  sleep 30
done

summary "### Migraciones: $label"
if [ -z "$before" ]; then
  echo "$label already has every migration of this version."
  summary "Ninguna pendiente: la base ya tenía todas las migraciones de esta versión."
  exit 0
fi

echo "Applying to $label:"
while read -r id; do echo "  $id"; done <<< "$before"
dotnet ef database update "${ef[@]}"

after=$(pending)
if [ -n "$after" ]; then
  echo "::error::$label still lacks migrations after dotnet ef database update: $(paste -sd ' ' <<< "$after")"
  exit 1
fi

echo "$label has every migration of this version."
summary "Aplicadas, en orden:"
while read -r id; do summary "- \`$id\`"; done <<< "$before"
