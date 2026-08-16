#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'geo uninstall: %s\n' "$1" >&2
  exit 1
}

purge=false
case $# in
  0) ;;
  1)
    [[ $1 == '--purge' ]] || fail 'unknown arguments.'
    purge=true
    ;;
  *) fail 'unknown arguments.' ;;
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

home=${HOME:-}
[[ $home == /* ]] || fail 'HOME must be an absolute path.'

config_home=${XDG_CONFIG_HOME:-"$home/.config"}
data_home=${XDG_DATA_HOME:-"$home/.local/share"}
cache_home=${XDG_CACHE_HOME:-"$home/.cache"}
[[ $config_home == /* ]] ||
  fail 'XDG_CONFIG_HOME must be an absolute path.'
[[ $data_home == /* ]] || fail 'XDG_DATA_HOME must be an absolute path.'
[[ $cache_home == /* ]] || fail 'XDG_CACHE_HOME must be an absolute path.'

application_root="$data_home/egress-geo"
application_directory="$application_root/app"
configuration_directory="$config_home/egress-geo"
cache_directory="$cache_home/egress-geo"
binary_directory="$home/.local/bin"
launcher_path="$binary_directory/geo"
unit_directory="$config_home/systemd/user"
update_service="$unit_directory/egress-geo-update.service"
update_timer="$unit_directory/egress-geo-update.timer"
enabled_timer="$unit_directory/timers.target.wants/egress-geo-update.timer"

remove_file() {
  local description=$1
  local path=$2

  if [[ -e $path || -L $path ]]; then
    rm -f -- "$path"
    printf 'Removed %s: %s\n' "$description" "$path"
  else
    printf 'Already absent %s: %s\n' "$description" "$path"
  fi
}

remove_directory() {
  local description=$1
  local path=$2

  if [[ -e $path || -L $path ]]; then
    rm -rf -- "$path"
    printf 'Removed %s: %s\n' "$description" "$path"
  else
    printf 'Already absent %s: %s\n' "$description" "$path"
  fi
}

if [[ -f $launcher_path ]] &&
  grep -Fqx '# Managed by egress-geo install.sh' "$launcher_path"; then
  remove_file 'geo launcher' "$launcher_path"
elif [[ -e $launcher_path || -L $launcher_path ]]; then
  printf 'Preserved unrecognized launcher: %s\n' "$launcher_path" >&2
else
  printf 'Already absent geo launcher: %s\n' "$launcher_path"
fi

remove_file 'update service' "$update_service"
remove_file 'update timer' "$update_timer"
remove_file 'enabled update timer' "$enabled_timer"
remove_directory 'geo application' "$application_directory"

if $purge; then
  remove_directory 'user configuration' "$configuration_directory"
  remove_directory 'user data' "$application_root"
  remove_directory 'user cache' "$cache_directory"
else
  printf 'Preserved user configuration: %s\n' "$configuration_directory"
  printf 'Preserved user data: %s\n' "$application_root"
  printf 'Preserved user cache: %s\n' "$cache_directory"
fi
