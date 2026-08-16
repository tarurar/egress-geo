#!/usr/bin/env bash
set -euo pipefail

script_directory=$(
  cd -- "$(dirname -- "${BASH_SOURCE[0]}")" || exit 1
  pwd
)
# shellcheck source=scripts/paths.sh
source "$script_directory/paths.sh"
egress_geo_resolve_paths install

repository_root=$(cd -- "$script_directory/.." && pwd)
project_path="$repository_root/src/EgressGeo/EgressGeo.csproj"
setup_script="$script_directory/setup.sh"
update_script="$script_directory/update.sh"
paths_script="$script_directory/paths.sh"

[[ -f $project_path ]] ||
  egress_geo_fail "project not found: $project_path"
[[ -f $setup_script ]] ||
  egress_geo_fail "setup wizard not found: $setup_script"
[[ -f $update_script ]] ||
  egress_geo_fail "update wrapper not found: $update_script"
[[ -f $paths_script ]] ||
  egress_geo_fail "path helper not found: $paths_script"
command -v dotnet >/dev/null 2>&1 ||
  egress_geo_fail 'dotnet was not found on PATH.'
command -v systemctl >/dev/null 2>&1 ||
  egress_geo_fail 'systemctl was not found on PATH.'
egress_geo_path_contains "$geo_binary_directory" ||
  egress_geo_fail \
    "add $geo_binary_directory to PATH before installing."
[[ ! -d $geo_launcher_path ]] ||
  egress_geo_fail "launcher path is a directory: $geo_launcher_path"

umask 022
mkdir -p -- "$geo_application_root" "$geo_binary_directory"

publish_directory=$(mktemp -d \
  "$geo_application_root/.app.publish.XXXXXX")
previous_directory=''
launcher_temporary=''

cleanup() {
  if [[ -n $launcher_temporary && -e $launcher_temporary ]]; then
    rm -f -- "$launcher_temporary"
  fi

  if [[ -n $publish_directory && -d $publish_directory ]]; then
    rm -rf -- "$publish_directory"
  fi

  if [[ -n $previous_directory ]] &&
    [[ -e $previous_directory || -L $previous_directory ]]; then
    if [[ ! -e $geo_application_directory ]]; then
      mv -- "$previous_directory" "$geo_application_directory"
    else
      rm -rf -- "$previous_directory"
    fi
  fi
}
trap cleanup EXIT

dotnet publish "$project_path" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained false \
  --output "$publish_directory"

install -m 0755 -- "$setup_script" "$publish_directory/setup.sh"
install -m 0755 -- "$update_script" "$publish_directory/update.sh"
install -m 0644 -- "$paths_script" "$publish_directory/paths.sh"

[[ -x $publish_directory/geo ]] ||
  egress_geo_fail 'publish did not produce an executable geo app host.'

if [[ -e $geo_application_directory || -L $geo_application_directory ]]; then
  previous_directory=$(mktemp -d \
    "$geo_application_root/.app.previous.XXXXXX")
  rmdir -- "$previous_directory"
  mv -- "$geo_application_directory" "$previous_directory"
fi
mv -- "$publish_directory" "$geo_application_directory"
publish_directory=''

launcher_temporary=$(mktemp \
  "$geo_binary_directory/.geo.launcher.XXXXXX")
{
  printf '%s\n' '#!/usr/bin/env bash'
  printf '%s\n' '# Managed by egress-geo install.sh'
  printf 'exec %q "$@"\n' "$geo_application_directory/geo"
} > "$launcher_temporary"
chmod 0755 "$launcher_temporary"
mv -T -- "$launcher_temporary" "$geo_launcher_path"
launcher_temporary=''

mkdir -p -- "$geo_unit_directory"
update_service="$geo_unit_directory/egress-geo-update.service"
update_timer="$geo_unit_directory/egress-geo-update.timer"
geo_updater_path="$geo_application_root/updater/geoipupdate"
geo_configuration_path="$geo_configuration_directory/GeoIP.conf"
geo_database_path="$geo_application_root/GeoLite2-City.mmdb"

systemd_quote() {
  local value=$1
  value=${value//\\/\\\\}
  value=${value//\"/\\\"}
  value=${value//%/%%}
  value=${value//$'\t'/\\t}
  printf '"%s"' "$value"
}

update_exec_start=$(printf '%s %s %s %s %s' \
  "$(systemd_quote "$geo_application_directory/update.sh")" \
  "$(systemd_quote "$geo_updater_path")" \
  "$(systemd_quote "$geo_configuration_path")" \
  "$(systemd_quote "$geo_database_path")" \
  "$(systemd_quote "$geo_application_directory/geo")")

cat > "$update_service" <<EOF
[Unit]
Description=Update the egress-geo GeoLite2 City database

[Service]
Type=oneshot
ExecStart=$update_exec_start
EOF
cat > "$update_timer" <<'EOF'
[Unit]
Description=Update the egress-geo GeoLite2 City database daily

[Timer]
OnCalendar=daily
Persistent=true
RandomizedDelaySec=6h

[Install]
WantedBy=timers.target
EOF
systemctl --user daemon-reload
systemctl --user enable --now egress-geo-update.timer

if [[ -n $previous_directory ]]; then
  rm -rf -- "$previous_directory"
  previous_directory=''
fi

printf 'Installed geo application: %s\n' "$geo_application_directory"
printf 'Installed geo launcher: %s\n' "$geo_launcher_path"
printf 'Uninstall with: %s\n' "$script_directory/uninstall.sh"
