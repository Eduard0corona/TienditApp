#!/usr/bin/env bash
# Lets this runner reach the Azure SQL server during one job: `open` adds a firewall rule for its public address and
# `close` removes it. Every rule of the deploy workflow is named github-actions-*, and `open` first removes the ones a
# cancelled or lost run left behind: the workflow never runs twice at the same time (its concurrency group).
#   $1: open or close
#   AZURE_SQL_RESOURCE_GROUP, AZURE_SQL_SERVER: the server (after az login)
set -euo pipefail

action="${1:?open or close}"
: "${AZURE_SQL_RESOURCE_GROUP:?}" "${AZURE_SQL_SERVER:?}"
server=(--resource-group "$AZURE_SQL_RESOURCE_GROUP" --server "$AZURE_SQL_SERVER")
rule="github-actions-${GITHUB_RUN_ID:?}-${GITHUB_RUN_ATTEMPT:?}-${GITHUB_JOB:?}"

rules() {
  az sql server firewall-rule list "${server[@]}" --query "[?starts_with(name, 'github-actions-')].name" --output tsv
}

remove() {
  az sql server firewall-rule delete "${server[@]}" --name "$1" --only-show-errors
}

case "$action" in
  open)
    leftovers=$(rules)
    for leftover in $leftovers; do
      echo "Removing $leftover, left by an earlier run."
      remove "$leftover"
    done
    address=$(curl -sS --max-time 20 https://api.ipify.org || true)
    if ! [[ "$address" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]]; then
      echo "::error::Could not learn this runner's public IPv4 address (got '$address')."
      exit 1
    fi
    az sql server firewall-rule create "${server[@]}" --name "$rule" \
      --start-ip-address "$address" --end-ip-address "$address" --only-show-errors --output none
    echo "$rule lets $address reach $AZURE_SQL_SERVER during this job."
    ;;
  close)
    existing=$(rules)
    if grep -Fxq "$rule" <<< "$existing"; then
      remove "$rule"
      echo "Removed $rule."
    fi
    ;;
  *)
    echo "::error::Unknown action '$action': use open or close."
    exit 1
    ;;
esac
