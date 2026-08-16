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

[[ -f $project_path ]] ||
  egress_geo_fail "project not found: $project_path"
command -v dotnet >/dev/null 2>&1 ||
  egress_geo_fail 'dotnet was not found on PATH.'
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

if [[ -n $previous_directory ]]; then
  rm -rf -- "$previous_directory"
  previous_directory=''
fi

printf 'Installed geo application: %s\n' "$geo_application_directory"
printf 'Installed geo launcher: %s\n' "$geo_launcher_path"
printf 'Uninstall with: %s\n' "$script_directory/uninstall.sh"
