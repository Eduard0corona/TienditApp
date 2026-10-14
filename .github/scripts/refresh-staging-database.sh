#!/usr/bin/env bash
# Replaces the staging database with a fresh copy of production on the same server, so each release rehearses its
# migrations on today's data before they reach production (docs/development/deploy-azure.md). Production is only
# read; the staging database is deleted and created again, so it can never be production or a system database.
#   AZURE_SQL_RESOURCE_GROUP, AZURE_SQL_SERVER: the server (after az login)
#   AZURE_SQL_DATABASE: production
#   AZURE_SQL_STAGING_DATABASE: the copy
#   AZURE_SQL_STAGING_SERVICE_OBJECTIVE (optional): the copy's tier, e.g. S0; empty keeps production's
set -euo pipefail

: "${AZURE_SQL_RESOURCE_GROUP:?}" "${AZURE_SQL_SERVER:?}"
production="${AZURE_SQL_DATABASE:?}"
staging="${AZURE_SQL_STAGING_DATABASE:?}"
server=(--resource-group "$AZURE_SQL_RESOURCE_GROUP" --server "$AZURE_SQL_SERVER")

# Database names are case-insensitive.
if [ "${staging,,}" = "${production,,}" ] || [ "${staging,,}" = "master" ]; then
  echo "::error::AZURE_SQL_STAGING_DATABASE ($staging) must be a database of its own: every release deletes it."
  exit 1
fi

databases=$(az sql db list "${server[@]}" --query "[].name" --output tsv)
if grep -Fxqi "$staging" <<< "$databases"; then
  echo "Deleting $staging, the copy of the previous release."
  az sql db delete "${server[@]}" --name "$staging" --yes --only-show-errors
fi

copy=(az sql db copy "${server[@]}" --name "$production" --dest-name "$staging" --only-show-errors --output none)
if [ -n "${AZURE_SQL_STAGING_SERVICE_OBJECTIVE:-}" ]; then
  copy+=(--service-objective "$AZURE_SQL_STAGING_SERVICE_OBJECTIVE")
fi
echo "Copying $production into $staging."
"${copy[@]}"
copied_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
echo "$staging is a copy of $production as of $copied_at."

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  echo "### Copia de producción" >> "$GITHUB_STEP_SUMMARY"
  echo "\`$staging\` es una copia de \`$production\` terminada a las $copied_at (UTC)." >> "$GITHUB_STEP_SUMMARY"
fi
