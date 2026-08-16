# egress-geo

`geo` reports the approximate city and country of the machine's current public
IPv4 egress. It discovers only the public address through ipify and resolves
that address locally with a user-provided GeoLite2 City database. No hosted
geolocation service receives the address.

IP geolocation is approximate. The reported city can represent a nearby
population center, network registration, or datacenter rather than a physical
location.

## Current lookup

The application targets .NET 10 on Linux x86-64. It expects the licensed
database at:

- `$XDG_DATA_HOME/egress-geo/GeoLite2-City.mmdb`, when `XDG_DATA_HOME` is set;
- `$HOME/.local/share/egress-geo/GeoLite2-City.mmdb` otherwise.

Run the source command with:

```console
dotnet run --project src/EgressGeo/EgressGeo.csproj
```

The rootless `geo setup` credential wizard is tracked separately and is not
part of this first lookup milestone. A production GeoLite database and MaxMind
credentials must never be added to this repository.

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
