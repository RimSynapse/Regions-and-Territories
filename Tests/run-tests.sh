#!/usr/bin/env bash
# Runs every R&T behaviour suite without RimWorld, Unity, Harmony or a game install.
#
# The suites exist because the 0.7 rules layers (Integration/, Placement/, Sizing/, Economy/) are
# deliberately dependency-free: no Find, no Harmony, no Unity, no TechLevel. That is what lets them
# be compiled against the hand-written doubles in RimWorldStubs.cs and run anywhere mono exists.
# Anything that genuinely needs the game is not tested here and is not pretended to be.
#
#   Ubuntu/WSL:  sudo apt-get install -y mono-mcs mono-runtime
#   Then:        Tests/run-tests.sh
#
# Exits non-zero if any suite fails to build or fails an assertion. Each binary is removed before
# its build so a compile failure can never be masked by a stale binary reporting a stale pass —
# that has happened here and produced a green run from a build that did not occur.
set -u

cd "$(dirname "$0")/.." || exit 1

SRC=Source
OUT="${TMPDIR:-/tmp}/rt-tests"
mkdir -p "$OUT"

MCS_FLAGS="-target:exe -langversion:latest -nowarn:0169,0414,0649,0219"
failures=0

run_suite() {
    name=$1; shift
    binary="$OUT/$name.exe"
    rm -f "$binary"

    if ! mcs $MCS_FLAGS -out:"$binary" "$@"; then
        echo "BUILD FAILED: $name"
        failures=$((failures + 1))
        return
    fi

    if ! mono "$binary"; then
        failures=$((failures + 1))
    fi
}

run_suite integration \
    Tests/RimWorldStubs.cs Tests/IntegrationTests.cs \
    $SRC/Integration/*.cs

run_suite placement \
    Tests/RimWorldStubs.cs Tests/PlacementTests.cs \
    $SRC/Integration/*.cs $SRC/Placement/*.cs

run_suite sizing \
    Tests/RimWorldStubs.cs Tests/SizingTests.cs \
    $SRC/Integration/*.cs $SRC/Sizing/*.cs

run_suite economy \
    Tests/RimWorldStubs.cs Tests/EconomyTests.cs \
    $SRC/Integration/*.cs $SRC/Sizing/*.cs $SRC/Economy/*.cs

run_suite taxation \
    Tests/RimWorldStubs.cs Tests/TaxationTests.cs \
    $SRC/Integration/*.cs $SRC/Sizing/*.cs $SRC/Economy/*.cs

run_suite military \
    Tests/RimWorldStubs.cs Tests/MilitaryTests.cs \
    $SRC/Integration/*.cs $SRC/Placement/*.cs $SRC/Military/*.cs

run_suite standing \
    Tests/RimWorldStubs.cs Tests/StandingTests.cs \
    $SRC/Integration/*.cs $SRC/Placement/*.cs $SRC/Sizing/*.cs $SRC/Standing/*.cs

run_suite scaling \
    Tests/RimWorldStubs.cs Tests/RimWorldStubsExt.cs Tests/ScalingTests.cs \
    $SRC/Integration/*.cs $SRC/Placement/*.cs $SRC/Sizing/*.cs $SRC/Economy/*.cs $SRC/Military/*.cs \
    $SRC/Standing/*.cs \
    $SRC/WorldObjectPlacementUtility.cs $SRC/OutpostPlacementUtility.cs \
    $SRC/SettlementSizeUtility.cs $SRC/RegionalOwnershipUtility.cs \
    $SRC/GeographicProvince.cs $SRC/IRegionDemographicProvider.cs \
    $SRC/ProductionScalingUtility.cs $SRC/TaxationUtility.cs \
    $SRC/MilitaryReachUtility.cs $SRC/ProvinceAdjacency.cs $SRC/FactionStandingUtility.cs

# Not a suite: a type-check over the impure files that cannot be behaviour-tested without a running
# game, but whose signatures can still be held to the shapes they will really meet. This is what
# catches a patch calling a method that no longer exists — the failure mode a mod cannot see until
# the game loads it.
echo
echo "== type-check (impure files, signatures only) =="
rm -f "$OUT/typecheck.dll"
if mcs -target:library -langversion:latest -nowarn:0169,0414,0649,0219,0067 -out:"$OUT/typecheck.dll" \
    Tests/RimWorldStubs.cs Tests/RimWorldStubsExt.cs \
    $SRC/Integration/*.cs $SRC/Placement/*.cs $SRC/Sizing/*.cs $SRC/Economy/*.cs $SRC/Military/*.cs \
    $SRC/Standing/*.cs \
    $SRC/WorldObjectPlacementUtility.cs $SRC/OutpostPlacementUtility.cs \
    $SRC/SettlementSizeUtility.cs $SRC/RegionalOwnershipUtility.cs \
    $SRC/GeographicProvince.cs $SRC/IRegionDemographicProvider.cs \
    $SRC/ProductionScalingUtility.cs $SRC/TaxationUtility.cs \
    $SRC/MilitaryReachUtility.cs $SRC/ProvinceAdjacency.cs $SRC/FactionStandingUtility.cs \
    $SRC/Patches/RegionsAndTerritories_EmpiresPatch.cs \
    $SRC/Patches/Patch_TileFinder_IsValidTileForNewSettlement.cs \
    $SRC/Patches/Patch_WorldInspectPane_TileInspectString.cs; then
    echo "  type-check clean"
else
    echo "  TYPE-CHECK FAILED"
    failures=$((failures + 1))
fi

echo
if [ "$failures" -eq 0 ]; then
    echo "ALL SUITES PASSED"
    exit 0
fi

echo "$failures SUITE(S) FAILED"
exit 1
