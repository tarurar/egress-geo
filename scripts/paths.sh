#!/usr/bin/env bash

egress_geo_fail() {
  printf 'geo %s: %s\n' "$geo_operation" "$1" >&2
  exit 1
}

egress_geo_resolve_paths() {
  geo_operation=$1
  geo_home=${HOME:-}
  [[ $geo_home == /* ]] ||
    egress_geo_fail 'HOME must be an absolute path.'

  geo_config_home=${XDG_CONFIG_HOME:-"$geo_home/.config"}
  geo_data_home=${XDG_DATA_HOME:-"$geo_home/.local/share"}
  geo_cache_home=${XDG_CACHE_HOME:-"$geo_home/.cache"}
  [[ $geo_config_home == /* ]] ||
    egress_geo_fail 'XDG_CONFIG_HOME must be an absolute path.'
  [[ $geo_data_home == /* ]] ||
    egress_geo_fail 'XDG_DATA_HOME must be an absolute path.'
  [[ $geo_cache_home == /* ]] ||
    egress_geo_fail 'XDG_CACHE_HOME must be an absolute path.'

  geo_application_root="$geo_data_home/egress-geo"
  geo_application_directory="$geo_application_root/app"
  geo_configuration_directory="$geo_config_home/egress-geo"
  geo_cache_directory="$geo_cache_home/egress-geo"
  geo_binary_directory="$geo_home/.local/bin"
  geo_launcher_path="$geo_binary_directory/geo"
  geo_unit_directory="$geo_config_home/systemd/user"
}

egress_geo_path_contains() {
  local expected=${1%/}
  local entry
  local -a entries=()

  IFS=: read -r -a entries <<< "${PATH:-}"
  for entry in "${entries[@]}"; do
    if [[ ${entry%/} == "$expected" ]]; then
      return 0
    fi
  done

  return 1
}
