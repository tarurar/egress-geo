#!/usr/bin/env bash
set -euo pipefail

script_directory=$(
  cd -- "$(dirname -- "${BASH_SOURCE[0]}")" || exit 1
  pwd
)
# shellcheck source=scripts/paths.sh
source "$script_directory/paths.sh"
egress_geo_resolve_paths uninstall

purge=false
case $# in
  0) ;;
  1)
    [[ $1 == '--purge' ]] || egress_geo_fail 'unknown arguments.'
    purge=true
    ;;
  *) egress_geo_fail 'unknown arguments.' ;;
esac

if $purge; then
  printf '%s\n' \
    'Purge permanently removes geo credentials, databases, and cache.'
  printf '%s\n' 'Type PURGE to confirm:'
  confirmation=''
  IFS= read -r confirmation || true
  if [[ $confirmation != 'PURGE' ]]; then
    printf '%s\n' 'Purge cancelled; nothing was removed.'
    exit 1
  fi
fi

remove_path() {
  local kind=$1
  local description=$2
  local path=$3

  if [[ -e $path || -L $path ]]; then
    case $kind in
      file) rm -f -- "$path" ;;
      directory) rm -rf -- "$path" ;;
      *) egress_geo_fail "unknown removal kind: $kind" ;;
    esac
    printf 'Removed %s: %s\n' "$description" "$path"
  else
    printf 'Already absent %s: %s\n' "$description" "$path"
  fi
}

expected_launcher=$(
  printf '%s\n' '#!/usr/bin/env bash'
  printf '%s\n' '# Managed by egress-geo install.sh'
  printf 'exec %q "$@"\n' "$geo_application_directory/geo"
)
launcher_contents=''
if [[ -f $geo_launcher_path ]]; then
  launcher_contents=$(< "$geo_launcher_path")
fi

if [[ $launcher_contents == "$expected_launcher" ]]; then
  remove_path file 'geo launcher' "$geo_launcher_path"
elif [[ -e $geo_launcher_path || -L $geo_launcher_path ]]; then
  printf 'Preserved unrecognized launcher: %s\n' \
    "$geo_launcher_path" >&2
else
  printf 'Already absent geo launcher: %s\n' "$geo_launcher_path"
fi

update_service="$geo_unit_directory/egress-geo-update.service"
update_timer="$geo_unit_directory/egress-geo-update.timer"
enabled_timer="$geo_unit_directory/timers.target.wants/egress-geo-update.timer"
remove_path file 'update service' "$update_service"
remove_path file 'update timer' "$update_timer"
remove_path file 'enabled update timer' "$enabled_timer"
remove_path directory 'geo application' "$geo_application_directory"

if $purge; then
  remove_path directory 'user configuration' \
    "$geo_configuration_directory"
  remove_path directory 'user data' "$geo_application_root"
  remove_path directory 'user cache' "$geo_cache_directory"
else
  printf 'Preserved user configuration: %s\n' \
    "$geo_configuration_directory"
  printf 'Preserved user data: %s\n' "$geo_application_root"
  printf 'Preserved user cache: %s\n' "$geo_cache_directory"
fi
