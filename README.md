# egress-geo

`geo` reports the approximate city and country of the machine's current public
IPv4 and IPv6 egress. It discovers both families concurrently, asks each
family-specific ipify endpoint first, and uses the corresponding ident.me
endpoint only after a failed or invalid response. The complete live attempt is
bounded to approximately two seconds. Addresses are resolved locally with a
user-provided GeoLite2 City database, so no hosted geolocation service receives
them for lookup.

IP geolocation is approximate. The reported city can represent a nearby
population center, network registration, or datacenter rather than a physical
location.

## Current lookup

The application targets .NET 10 on Linux x86-64. It expects the licensed
database at:

- `$XDG_DATA_HOME/egress-geo/GeoLite2-City.mmdb`, when `XDG_DATA_HOME` is set;
- `$HOME/.local/share/egress-geo/GeoLite2-City.mmdb` otherwise.

The last successful egress snapshot is kept at:

- `$XDG_CACHE_HOME/egress-geo/snapshot.json`, when `XDG_CACHE_HOME` is set;
- `$HOME/.cache/egress-geo/snapshot.json` otherwise.

The cache directory and file are user-private. Snapshot replacement uses an
atomic rename so an interrupted write cannot leave a partial cache file. A
valid snapshot records usable locations for both address families, so a
one-family result cannot replace the last complete dual-stack observation.

Run the source command with:

```console
dotnet run --project src/EgressGeo/EgressGeo.csproj
```

## Rootless installation

Install or repair `geo` from a source checkout with the .NET 10 SDK available:

```console
./scripts/install.sh
```

The installer publishes a framework-dependent Linux x86-64 application to
`$XDG_DATA_HOME/egress-geo/app`, or
`$HOME/.local/share/egress-geo/app` when `XDG_DATA_HOME` is unset. It installs
the launcher at `$HOME/.local/bin/geo`; installation stops before publishing
unless that directory is already on `PATH`.
The launcher refers only to the published application and setup wizard, so the
source checkout is not needed to run the installed command. Re-running the
installer replaces stale publish sidecars and repairs the launcher without
duplicating either.

Configure GeoLite after installation:

```console
geo setup
```

The three-stage wizard opens MaxMind's GeoLite-specific signup, account
information, and license-key pages. It waits for the human to complete account
and email verification, captures the numeric account ID visibly, and captures
the license key with hidden terminal input. It never asks for the MaxMind
account password.

The wizard writes only the required `geoipupdate` settings to
`$XDG_CONFIG_HOME/egress-geo/GeoIP.conf`, or
`$HOME/.config/egress-geo/GeoIP.conf` when `XDG_CONFIG_HOME` is unset. The file
is mode `0600`. Credentials are read from that file by the updater; they are
not passed as command-line arguments.

Setup downloads MaxMind's official `geoipupdate` 8.0.0 Linux amd64 archive,
checks its pinned SHA-256 value before activation, and installs the executable
under user application data. It then downloads only `GeoLite2-City` and runs
the installed `geo` command to verify the database. A failed download or
checksum check preserves an existing updater, database, and active
configuration. On a re-run, press Enter at either credential prompt to keep a
valid saved value; missing updater or database assets are repaired.

Installation also writes `egress-geo-update.service` and
`egress-geo-update.timer` to the user systemd unit directory and enables the
timer. The timer runs daily with up to six hours of randomized delay. It is
persistent, so a missed run is recovered after the machine comes back. Repair
installation rewrites the units, reloads the user manager, and enables the
timer again without duplicating it.

The update service downloads into a temporary directory on the same filesystem
as the active database. It suppresses updater output, verifies the candidate
with the installed `geo` command, and atomically replaces the active database
only after verification. Failed and no-change runs preserve the current
database. Journal output contains only generic start, no-change, success, or
failure boundaries; credentials, configuration contents, and updater URLs are
not logged.

Inspect the complete installed system with:

```console
geo doctor
```

The doctor checks the application, database readability and build age,
updater, private credential permissions, installed/enabled/active timer, cache,
and all configured public-IP endpoints. Endpoint probes share a two-second
deadline. Missing IPv6 is reported as an informational capability result, not
as a failed installation. The credential file is inspected only for its file
type and mode; its contents are never read into diagnostic output.

A database more than 30 days past its embedded build date is reported as
stale, matching the GeoLite requirement to stop using and destroy old versions
within 30 days of an update. Healthy diagnostics exit `0`. Actionable failures
are all reported in one run and produce exit `1`.

Run the idempotent uninstaller from the source checkout with:

```console
./scripts/uninstall.sh
```

Default uninstall removes the launcher, published application, and known
user-systemd unit files after disabling and stopping the update timer. It
preserves configuration and credentials under
`$XDG_CONFIG_HOME/egress-geo`, the database and updater data under
`$XDG_DATA_HOME/egress-geo`, and snapshots under
`$XDG_CACHE_HOME/egress-geo`, using the corresponding directories below
`$HOME` when an XDG variable is unset. Pass `--purge` to remove those retained
directories as well; purge proceeds only after `PURGE` is entered exactly.
Installation, uninstall, and purge require no `sudo` and are safe to repeat.

When both families resolve to the same city and country, human output shares
one location line. Different cities receive separate family rows. Different
countries also produce a possible-VPN-leak warning and exit `2`; an ordinary
live result exits `0`, a cached-only result exits `3`, and no usable result
exits `1`.

Live address discovery still runs on every lookup. When an address is current
but GeoLite cannot resolve it, location data may be reused only from an exact
address match in a cache no older than 24 hours. When both live address-family
probes fail, the complete recent snapshot is shown with a prominent cached
marker and readable age. Older, malformed, or semantically invalid snapshots
are ignored.

Use `--json` for the stable machine-readable form:

```json
{
  "status": "healthy",
  "observedAt": "2026-08-16T12:34:56+00:00",
  "cached": false,
  "cacheAgeSeconds": null,
  "warnings": [],
  "families": [
    {
      "family": "IPv4",
      "address": "203.0.113.7",
      "approximateCity": "Manama",
      "countryCode": "BH",
      "discoverySource": "ipify"
    }
  ]
}
```

`status` is `healthy`, `country-mismatch`, `cached`, or `failed`. A country
mismatch adds `possible-vpn-leak` to `warnings`. Fully live results set
`cached` to `false` and `cacheAgeSeconds` to `null`; exact-address location
reuse and cached-only fallback set `cached` to `true` and report the snapshot
age. The `families` array contains one entry per discovered or cached address;
unavailable city or country values are explicit JSON `null` values.

A production GeoLite database and MaxMind credentials must never be added to
this repository.

## Development

```console
mise run dotnet:build
mise run dotnet:test
dotnet publish src/EgressGeo/EgressGeo.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained false
```

Tests use fakes for HTTP and a synthetic MaxMind DB fixture. They make no live
network requests and require no MaxMind credentials.

## Attribution and licenses

This product includes GeoLite Data created by MaxMind, available from
<https://www.maxmind.com>.

The application source is licensed under the [MIT License](LICENSE). The
synthetic fixture is separate upstream test data; its source, checksum, and
license notice are recorded in
[`tests/EgressGeo.Tests/Fixtures/THIRD-PARTY-NOTICES.md`](tests/EgressGeo.Tests/Fixtures/THIRD-PARTY-NOTICES.md).
