#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 4 ]]; then
  printf '%s\n' 'geo update: invalid service configuration.' >&2
  exit 1
fi

readonly geo_updater_path=$1
readonly geo_configuration_path=$2
readonly geo_database_path=$3
readonly geo_application_path=$4

for required_path in \
  "$geo_updater_path" \
  "$geo_configuration_path" \
  "$geo_database_path" \
  "$geo_application_path"; do
  [[ $required_path == /* ]] || {
    printf '%s\n' 'geo update: invalid service configuration.' >&2
    exit 1
  }
done

update_workspace=''
update_completed=false

cleanup_update() {
  local exit_code=$?
  if [[ -n $update_workspace && -d $update_workspace ]]; then
    rm -rf -- "$update_workspace" || true
  fi
  if ! $update_completed; then
    printf '%s\n' \
      'geo update: failed; previous database preserved.' >&2
  fi
  exit "$exit_code"
}
trap cleanup_update EXIT

printf '%s\n' 'geo update: started.'

[[ -x $geo_updater_path && -r $geo_configuration_path ]] || exit 1
[[ -x $geo_application_path ]] || exit 1

geo_database_directory=$(dirname -- "$geo_database_path")
mkdir -p -- "$geo_database_directory"
update_workspace=$(mktemp -d \
  "$geo_database_directory/.update.XXXXXX")
staged_database_directory="$update_workspace/egress-geo"
staged_database_path="$staged_database_directory/GeoLite2-City.mmdb"
mkdir -p -- "$staged_database_directory"

if [[ -f $geo_database_path ]]; then
  cp --preserve=mode,timestamps -- \
    "$geo_database_path" "$staged_database_path"
fi

"$geo_updater_path" \
  -f "$geo_configuration_path" \
  -d "$staged_database_directory" \
  >/dev/null 2>&1 || exit 1

XDG_DATA_HOME="$update_workspace" \
  "$geo_application_path" setup --verify-database \
  >/dev/null 2>&1 || exit 1

if [[ -f $geo_database_path ]] &&
  cmp --silent -- "$geo_database_path" "$staged_database_path"; then
  update_completed=true
  printf '%s\n' \
    'geo update: no update available; current database preserved.'
  exit 0
fi

mv -T -- "$staged_database_path" "$geo_database_path"
update_completed=true
printf '%s\n' 'geo update: database updated and verified.'
