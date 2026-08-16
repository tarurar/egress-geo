#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'geo install: %s\n' "$1" >&2
  exit 1
}

home=${HOME:-}
[[ $home == /* ]] || fail 'HOME must be an absolute path.'

data_home=${XDG_DATA_HOME:-"$home/.local/share"}
[[ $data_home == /* ]] || fail 'XDG_DATA_HOME must be an absolute path.'

script_directory=$(
  cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
  pwd
)
repository_root=$(cd -- "$script_directory/.." && pwd)
project_path="$repository_root/src/EgressGeo/EgressGeo.csproj"
uninstaller_path="$script_directory/uninstall.sh"
application_root="$data_home/egress-geo"
application_directory="$application_root/app"
binary_directory="$home/.local/bin"
launcher_path="$binary_directory/geo"

[[ -f $project_path ]] || fail "project not found: $project_path"
[[ -f $uninstaller_path ]] || fail "uninstaller not found: $uninstaller_path"
command -v dotnet >/dev/null 2>&1 || fail 'dotnet was not found on PATH.'

umask 022
mkdir -p -- "$application_root" "$binary_directory"

publish_directory=$(mktemp -d "$application_root/.app.publish.XXXXXX")
previous_directory=''
launcher_temporary=''

cleanup() {
  if [[ -n $launcher_temporary && -e $launcher_temporary ]]; then
    rm -f -- "$launcher_temporary"
  fi

  if [[ -n $publish_directory && -d $publish_directory ]]; then
    rm -rf -- "$publish_directory"
  fi

  if [[ -n $previous_directory && -e $previous_directory ]]; then
    if [[ ! -e $application_directory ]]; then
      mv -- "$previous_directory" "$application_directory"
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
  fail 'publish did not produce an executable geo app host.'
install -m 0755 -- "$uninstaller_path" "$publish_directory/uninstall.sh"

if [[ -e $application_directory || -L $application_directory ]]; then
  previous_directory=$(mktemp -d "$application_root/.app.previous.XXXXXX")
  rmdir -- "$previous_directory"
  mv -- "$application_directory" "$previous_directory"
fi
mv -- "$publish_directory" "$application_directory"
publish_directory=''

launcher_temporary=$(mktemp "$binary_directory/.geo.launcher.XXXXXX")
{
  printf '%s\n' '#!/usr/bin/env bash'
  printf '%s\n' '# Managed by egress-geo install.sh'
  printf 'exec %q "$@"\n' "$application_directory/geo"
} > "$launcher_temporary"
chmod 0755 "$launcher_temporary"
mv -- "$launcher_temporary" "$launcher_path"
launcher_temporary=''

if [[ -n $previous_directory ]]; then
  rm -rf -- "$previous_directory"
  previous_directory=''
fi

printf 'Installed geo application: %s\n' "$application_directory"
printf 'Installed geo launcher: %s\n' "$launcher_path"
printf 'Uninstall with: %s\n' "$application_directory/uninstall.sh"

case ":${PATH:-}:" in
  *":$binary_directory:"*) ;;
  *)
    printf 'geo install: add %s to PATH.\n' "$binary_directory" >&2
    ;;
esac
