#!/usr/bin/env bash
# Consumer smoke check for the Funcular.Data.Orm package (docs/plans/DECISIONS-3.10.2.md FD-05, backlog WP-E1).
# The bash twin of Run-ConsumerSmoke.ps1, for git-bash and Linux CI: packs Funcular.Data.Orm.SqlServer into a local
# feed under the temp directory, copies the ConsumerSmoke console app next to it, restores it against that feed plus
# nuget.org (so the drivers the package declares resolve at their declared minimums, never higher), and runs it for
# net8.0 (every provider) and, on Windows, net48 (SQL Server only). Nothing is published anywhere.
#
# Usage: bash tools/consumer-smoke/run-consumer-smoke.sh [--version 3.12.1] [--configuration Release]
#          [--skip-net48] [--skip-float-check] [--keep] [--expected "Microsoft.Data.SqlClient=6.0.0 Npgsql=9.0.5 ..."]
set -u

version="3.12.1"
configuration="Release"
expected="Microsoft.Data.SqlClient=6.0.0 Npgsql=9.0.5 MySqlConnector=2.6.2 Microsoft.Data.Sqlite=8.0.32"
skip_net48=0
skip_float_check=0
keep=0
while [ $# -gt 0 ]; do
  case "$1" in
    --version) version="$2"; shift 2 ;;
    --configuration) configuration="$2"; shift 2 ;;
    --expected) expected="$2"; shift 2 ;;
    --skip-net48) skip_net48=1; shift ;;
    --skip-float-check) skip_float_check=1; shift ;;
    --keep) keep=1; shift ;;
    *) echo "unknown argument: $1"; exit 64 ;;
  esac
done

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
projects="Funcular.Data.Orm.Core/Funcular.Data.Orm.Core.csproj Funcular.Data.Orm.SqlServer/Funcular.Data.Orm.SqlServer.csproj Funcular.Data.Orm.PostgreSql/Funcular.Data.Orm.PostgreSql.csproj Funcular.Data.Orm.MySql/Funcular.Data.Orm.MySql.csproj Funcular.Data.Orm.Sqlite/Funcular.Data.Orm.Sqlite.csproj"

floating=0
for project in $projects; do
  if grep -nE '<PackageReference [^>]*Version="[^"]*\*' "$repo/$project" >/dev/null; then
    echo "FAIL: floating package reference(s) in $project:"
    grep -nE '<PackageReference [^>]*Version="[^"]*\*' "$repo/$project"
    floating=1
  fi
done
if [ "$floating" = "1" ]; then
  if [ "$skip_float_check" = "0" ]; then exit 2; fi
  echo "(continuing: --skip-float-check)"
fi

tmp="${TMPDIR:-${TEMP:-${TMP:-/tmp}}}"
work="$tmp/funky-smoke-$(date +%s)-$$"
feed="$work/feed"
app="$work/app"
mkdir -p "$feed" "$app"
echo "Work directory: $work"

echo "Packing Funcular.Data.Orm $version ($configuration) into the local feed..."
dotnet pack "$repo/Funcular.Data.Orm.SqlServer/Funcular.Data.Orm.SqlServer.csproj" -c "$configuration" -o "$feed" --nologo -v q || { echo "FAIL: pack"; exit 3; }
[ -f "$feed/Funcular.Data.Orm.$version.nupkg" ] || { echo "FAIL: $feed/Funcular.Data.Orm.$version.nupkg not produced (is <Version> $version?)"; exit 3; }

cp -r "$here/ConsumerSmoke/." "$app/"
# nuget.config expands %FUNKY_SMOKE_FEED%; NuGet wants a native path on Windows.
if command -v cygpath >/dev/null 2>&1; then export FUNKY_SMOKE_FEED="$(cygpath -w "$feed")"; else export FUNKY_SMOKE_FEED="$feed"; fi

# A packages folder of its own: the machine's global cache may already hold a Funcular.Data.Orm of this version (an
# earlier pack, or the published one), and NuGet would use that instead of the package just packed.
packages="$work/packages"
if command -v cygpath >/dev/null 2>&1; then packages="$(cygpath -w "$packages")"; fi

failed=0
frameworks="net8.0"
if [ "$skip_net48" = "0" ] && [ "${OS:-}" = "Windows_NT" ]; then frameworks="net8.0 net48"; fi
for framework in $frameworks; do
  echo ""
  echo "=== $framework ==="
  # shellcheck disable=SC2086
  if dotnet run --project "$app/ConsumerSmoke.csproj" -c "$configuration" -f "$framework" -p:FunkyVersion="$version" -p:RestorePackagesPath="$packages" --nologo -v q -- $expected; then
    echo "PASS: $framework"
  else
    echo "FAIL: $framework"; failed=1
  fi
done

if [ "$keep" = "0" ]; then rm -rf "$work"; else echo "Kept: $work"; fi
if [ "$failed" = "1" ]; then exit 1; fi
echo ""
echo "PASS: the package installs alone and every provider loads its driver at the declared minimum."
exit 0
