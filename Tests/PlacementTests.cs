// Behaviour tests for the 0.7 placement governance layer (Epic 2).
//
// PlacementEvaluator is pure by design, so this suite needs no RimWorld at all: tiles are points
// on a line, distance is |a-b|, and factions are plain strings. What is being tested is the rules,
// which is the part that can actually be wrong.
using System;
using System.Collections.Generic;
using System.Linq;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Placement;

namespace PlacementTests
{
    public static class Program
    {
        private static int failures;

        private const string Player = "player";
        private const string Empire = "player-empire";
        private const string Rival = "rival";

        public static int Main()
        {
            Section("an empty world refuses nothing");
            var empty = World();
            Check("settlement on bare ground", Allowed(empty, 10, Player, WorldObjectKind.Settlement));
            Check("outpost on bare ground", Allowed(empty, 10, Player, WorldObjectKind.Outpost));

            Section("buffer between permanent holdings");
            var crowded = World(Holding(0, WorldObjectKind.Settlement, Rival));
            Check("outpost one tile away is refused", Refused(crowded, 1, Player, WorldObjectKind.Outpost, PlacementRejection.TooCloseToHolding));
            Check("outpost two tiles away is fine", Allowed(crowded, 2, Player, WorldObjectKind.Outpost));
            Check("settlement one tile away is refused too", Refused(crowded, 1, Player, WorldObjectKind.Settlement, PlacementRejection.TooCloseToHolding));
            Check("the refusal names the neighbour", Reason(crowded, 1, Player, WorldObjectKind.Outpost).Contains("settlement"));

            Section("military installations hold ground (new in 0.7)");
            var garrisoned = World(Holding(0, WorldObjectKind.Military, Rival));
            Check("outpost beside a garrison is refused", Refused(garrisoned, 1, Player, WorldObjectKind.Outpost, PlacementRejection.TooCloseToHolding));
            Check("military beside a garrison is refused", Refused(garrisoned, 1, Player, WorldObjectKind.Military, PlacementRejection.TooCloseToHolding));

            Section("camps are exempt from the buffer, both ways");
            Check("a camp may be pitched beside a garrison", Allowed(garrisoned, 1, Player, WorldObjectKind.Camp));
            var camped = World(Holding(0, WorldObjectKind.Camp, Rival));
            Check("a settlement may be built beside a camp", Allowed(camped, 1, Player, WorldObjectKind.Settlement));

            Section("non-territorial objects are never governed");
            Check("caravan", Allowed(crowded, 0, Player, WorldObjectKind.Caravan));
            Check("quest site", Allowed(crowded, 0, Player, WorldObjectKind.Site));
            Check("unclassified object", Allowed(crowded, 0, Player, WorldObjectKind.Unknown));

            Section("supply range");
            var anchored = World(Holding(0, WorldObjectKind.Settlement, Player));
            Check("at the supply limit", Allowed(anchored, PlacementRules.MaxSupplyDistance, Player, WorldObjectKind.Outpost));
            Check("one tile past it", Refused(anchored, PlacementRules.MaxSupplyDistance + 1, Player, WorldObjectKind.Outpost, PlacementRejection.OutOfSupplyRange));
            Check("a faction with nothing anywhere is exempt", Allowed(anchored, 100, Rival, WorldObjectKind.Outpost));
            Check("camps need no supply line", Allowed(anchored, 100, Player, WorldObjectKind.Camp));

            Section("supply runs from held borders, not just holdings");
            var bordered = World(Holding(0, WorldObjectKind.Settlement, Player));
            bordered.HeldBorderTiles = f => Equals(f, Player) ? new[] { 50 } : new int[0];
            Check("a tile beside the border is supplied", Allowed(bordered, 52, Player, WorldObjectKind.Outpost));
            Check("a tile far from both is not", Refused(bordered, 200, Player, WorldObjectKind.Outpost, PlacementRejection.OutOfSupplyRange));

            Section("empire-style player factions share supply (Epic 1 feeds Epic 2)");
            var splitPlayer = World(Holding(0, WorldObjectKind.Settlement, Empire));
            splitPlayer.FactionsMatch = (a, b) => IsPlayerSide(a) && IsPlayerSide(b);
            Check("a player outpost draws supply from an empire colony", Allowed(splitPlayer, 5, Player, WorldObjectKind.Outpost));
            Check("...and is held to that colony's supply range",
                Refused(splitPlayer, 100, Player, WorldObjectKind.Outpost, PlacementRejection.OutOfSupplyRange));
            var noMatch = World(Holding(0, WorldObjectKind.Settlement, Empire));
            Check("without the equivalence the colony anchors nothing", Allowed(noMatch, 100, Player, WorldObjectKind.Outpost));

            Section("territory ownership gates permanent holdings");
            var foreign = World();
            foreign.ProvinceIdAt = t => 1;
            foreign.ControlOf = (p, f) => ProvinceControl.Foreign;
            Check("settlement refused on foreign ground", Refused(foreign, 10, Player, WorldObjectKind.Settlement, PlacementRejection.ForeignTerritory));
            Check("outpost refused on foreign ground", Refused(foreign, 10, Player, WorldObjectKind.Outpost, PlacementRejection.ForeignTerritory));
            Check("camp may still trespass", Allowed(foreign, 10, Player, WorldObjectKind.Camp));

            Section("contested ground is not foreign ground");
            var contested = World();
            contested.ProvinceIdAt = t => 1;
            contested.ControlOf = (p, f) => ProvinceControl.Contested;
            Check("placement allowed", Allowed(contested, 10, Player, WorldObjectKind.Settlement));
            Check("and flagged as contested", Evaluate(contested, 10, Player, WorldObjectKind.Settlement).Contested);
            Check("held ground is not flagged", !Evaluate(empty, 10, Player, WorldObjectKind.Settlement).Contested);

            Section("sequential expansion");
            var provinces = World(Holding(0, WorldObjectKind.Settlement, Player));
            // Provinces are three tiles wide, so every case below stays inside supply range and
            // the only thing under test is province adjacency.
            provinces.ProvinceIdAt = t => t < 3 ? 1 : (t < 6 ? 2 : 3);
            provinces.ProvincesAdjacent = (a, b) => Math.Abs(a - b) <= 1;
            provinces.ControlOf = (p, f) => ProvinceControl.Unclaimed;
            Check("into the same province", Allowed(provinces, 2, Player, WorldObjectKind.Settlement));
            Check("into an adjacent province", Allowed(provinces, 4, Player, WorldObjectKind.Settlement));
            Check("across a gap is refused", Refused(provinces, 7, Player, WorldObjectKind.Settlement, PlacementRejection.NoAdjacentFoothold));
            Check("camps ignore the rule", Allowed(provinces, 7, Player, WorldObjectKind.Camp));
            Check("a faction with no holdings ignores it", Allowed(provinces, 7, Rival, WorldObjectKind.Settlement));

            Section("a province you already hold is always reachable");
            var held = World(Holding(0, WorldObjectKind.Settlement, Player));
            held.ProvinceIdAt = t => t < 3 ? 1 : 3;
            held.ProvincesAdjacent = (a, b) => false;
            held.ControlOf = (p, f) => p == 3 ? ProvinceControl.Held : ProvinceControl.Unclaimed;
            Check("a non-adjacent province you own is still settleable", Allowed(held, 4, Player, WorldObjectKind.Settlement));

            Section("rule precedence");
            var stacked = World(Holding(0, WorldObjectKind.Settlement, Rival));
            stacked.ProvinceIdAt = t => 1;
            stacked.ControlOf = (p, f) => ProvinceControl.Foreign;
            Check("crowding is reported before ownership",
                Evaluate(stacked, 1, Player, WorldObjectKind.Settlement).Rejection == PlacementRejection.TooCloseToHolding);
            Check("ownership is reported before supply range",
                Evaluate(stacked, 400, Player, WorldObjectKind.Settlement).Rejection == PlacementRejection.ForeignTerritory);

            Section("degenerate input");
            Check("a null world allows everything", PlacementEvaluator.Evaluate(null, 0, Player, WorldObjectKind.Settlement).Allowed);
            var nullFaction = World(Holding(0, WorldObjectKind.Settlement, Player));
            Check("a null placing faction is not matched to anything",
                Allowed(nullFaction, 500, null, WorldObjectKind.Settlement));
            var nullHolding = World(Holding(0, WorldObjectKind.Settlement, Player));
            nullHolding.Holdings.Add(null);
            Check("a null holding in the list is skipped", Allowed(nullHolding, 5, Player, WorldObjectKind.Outpost));

            Section("rule table");
            Check("permanent pairs are separated", PlacementRules.MinSeparation(WorldObjectKind.Settlement, WorldObjectKind.Military) == PlacementRules.PermanentHoldingSeparation);
            Check("camp pairs are not", PlacementRules.MinSeparation(WorldObjectKind.Camp, WorldObjectKind.Settlement) == 0);
            Check("outposts need supply", PlacementRules.RequiresSupplyLine(WorldObjectKind.Outpost));
            Check("camps do not", !PlacementRules.RequiresSupplyLine(WorldObjectKind.Camp));
            Check("the contest margin is below the ownership threshold", PlacementRules.ContestMargin < PlacementRules.OwnershipThreshold);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PLACEMENT TESTS PASSED" : failures + " PLACEMENT TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        // -- harness ----------------------------------------------------------

        private static bool IsPlayerSide(object f)
        {
            return Equals(f, Player) || Equals(f, Empire);
        }

        private static PlacementHolding Holding(int tile, WorldObjectKind kind, object faction)
        {
            return new PlacementHolding(tile, kind, faction);
        }

        private static PlacementWorld World(params PlacementHolding[] holdings)
        {
            return new PlacementWorld
            {
                Distance = (a, b) => Math.Abs(a - b),
                Holdings = holdings.ToList(),
                ProvinceIdAt = t => -1,
                ControlOf = (p, f) => ProvinceControl.Unclaimed,
                HeldBorderTiles = f => new int[0],
                ProvincesAdjacent = (a, b) => false
            };
        }

        private static PlacementDecision Evaluate(PlacementWorld world, int tile, object faction, WorldObjectKind kind)
        {
            return PlacementEvaluator.Evaluate(world, tile, faction, kind);
        }

        private static bool Allowed(PlacementWorld world, int tile, object faction, WorldObjectKind kind)
        {
            return Evaluate(world, tile, faction, kind).Allowed;
        }

        private static bool Refused(PlacementWorld world, int tile, object faction, WorldObjectKind kind, PlacementRejection expected)
        {
            PlacementDecision d = Evaluate(world, tile, faction, kind);
            return !d.Allowed && d.Rejection == expected;
        }

        private static string Reason(PlacementWorld world, int tile, object faction, WorldObjectKind kind)
        {
            return Evaluate(world, tile, faction, kind).Reason ?? string.Empty;
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
