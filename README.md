# egress-geo

`geo` reports the approximate city and country of the machine's current public
IPv4 and IPv6 egress. It discovers both families concurrently, asks each
family-specific deSEC endpoint first, and uses the corresponding Joker endpoint
only after a failed, timed-out, or invalid response. The complete live attempt
is bounded to approximately two seconds. Addresses are resolved locally with a
locally installed GeoLite2 City database, so no hosted geolocation service
receives them for lookup.

IP geolocation is approximate. The reported city can represent a nearby
population center, network registration, or datacenter rather than a physical
location.

## Current lookup

The application targets .NET 10 on Linux x86-64. Verified databases are kept
under:

- `$XDG_DATA_HOME/egress-geo/databases`, when `XDG_DATA_HOME` is set;
- `$HOME/.local/share/egress-geo/databases` otherwise.

Each database is named by its SHA-256 digest. The private provenance file
selects the active digest, so the database and the metadata describing it
change as one atomic installation state. A legacy `GeoLite2-City.mmdb` beside
that directory remains readable until the first successful `geo setup`
migrates it.

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
The launcher refers only to the published application, so the source checkout
is not needed to run the installed command. Re-running the installer replaces
stale publish sidecars and repairs the launcher without duplicating either.

Configure GeoLite after installation:

```console
geo setup
```

Setup is non-interactive and credential-free. It opens no browser, requests no
MaxMind account or license key, creates no `GeoIP.conf`, and installs no
`geoipupdate`. Instead, it resolves the latest dated release from
[`P3TERX/GeoLite.mmdb`](https://github.com/P3TERX/GeoLite.mmdb), requires exactly
one `GeoLite2-City.mmdb` asset, and downloads that asset from its GitHub Release
URL.

P3TERX is a third-party republisher, not an official MaxMind service. P3TERX
plus GitHub Releases is the accepted database-distribution boundary for this
tool. The SHA-256 digest supplied by the GitHub Release API detects corruption
or mismatch within that boundary; it is not an independent proof that MaxMind
produced the bytes.

Before activation, `geo` verifies the downloaded digest, parses the candidate
as a GeoLite2 City MMDB, checks its embedded build time, and rejects stale data
or a rollback. The private candidate is moved to an immutable digest-named path
only after every check passes. A temporary provenance file is then renamed
atomically to select that matching database and metadata together. An
interruption before the pointer rename leaves the previous pair selected; any
metadata, HTTP, digest, parse, or activation failure therefore preserves the
last known-good database. An identical release is a successful no-change
result. Setup and timer runs hold an exclusive update lock, so their rollback
checks and activation cannot interleave. Inactive managed database revisions
are removed by subsequent maintenance after a one-hour reader grace period.

Private provenance is recorded beside the database at
`$XDG_DATA_HOME/egress-geo/provenance.json`, or
`$HOME/.local/share/egress-geo/provenance.json`. It contains the P3TERX
repository and release tag, publication time, GitHub asset URL and digest,
database build time, and local activation time. Release response bodies and
unrelated metadata are never persisted.

Installation also writes `egress-geo-update.service` and
`egress-geo-update.timer` to the user systemd unit directory and enables the
timer. The timer runs daily with up to six hours of randomized delay. It is
persistent, so a missed run is recovered after the machine comes back. Repair
installation rewrites the units, reloads the user manager, and enables the
timer again without duplicating it.

The update service invokes the same verified acquisition path as `geo setup`.
Failed and no-change runs preserve the current database. Journal output contains
only generic start, no-change, success, or failure boundaries; Release API
bodies and asset URLs are not logged.

### Migrating an older installation

Re-run `./scripts/install.sh`, then run `geo setup`. Repair installation
replaces the user-systemd units so scheduled maintenance uses the
credential-free P3TERX path. The application ignores existing `GeoIP.conf` and
`geoipupdate` data but leaves it untouched. A legacy flat
`GeoLite2-City.mmdb` remains available to existing readers until setup activates
the verified digest-named database. Run `geo doctor` after setup to verify the
database, provenance, source, timer, cache, and endpoint state together.

Inspect the complete installed system with:

```console
geo doctor
```

The doctor checks the application, database readability and build age, private
provenance shape and digest, P3TERX source reachability, the
installed/enabled/active timer, cache, and all configured public-IP endpoints.
The endpoint check probes the family-specific deSEC and Joker endpoints within
one bounded budget and never includes response bodies in output. Missing IPv6
is reported as an informational capability result, not as a failed installation.

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
preserves the database, provenance, cache, and any legacy `GeoIP.conf` or
`geoipupdate` files from an older installation. Pass `--purge` to remove all of
that retained user configuration, data, and cache; purge proceeds only after
`PURGE` is entered exactly. Installation, uninstall, and purge require no
`sudo` and are safe to repeat.

## CLI and exit contracts

Run `geo` for human-readable output or `geo --json` for the stable machine
contract. `geo setup` installs or updates the database, `geo doctor` checks the
complete installation, and `geo --help` and `geo --version` expose the command
contract and installed version.

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
      "discoverySource": "deSEC"
    }
  ]
}
```

`status` is `healthy`, `country-mismatch`, `cached`, or `failed`. A country
mismatch adds `possible-vpn-leak` to `warnings`. Fully live results set
`cached` to `false` and `cacheAgeSeconds` to `null`; exact-address location
reuse and cached-only fallback set `cached` to `true` and report the snapshot
age. A missing, unreadable, or stale database still produces this JSON contract
with `status` set to `failed` and an empty `families` array; exit `1` and stderr
provide the `geo setup` remediation. New discoveries record `discoverySource`
as `deSEC` for primary success or `Joker` for fallback success. Snapshots from
older versions with `ipify` or `ident.me` remain readable until their normal
24-hour expiry. The `families` array contains one entry per discovered or cached
address; unavailable city or country values are explicit JSON `null` values.

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
