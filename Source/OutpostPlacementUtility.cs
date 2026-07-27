using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimSynapse.RegionsAndTerritories
{
    public static class OutpostPlacementUtility
    {
        public const int MAX_SUPPLY_DISTANCE = 8;
        public const int MIN_BUFFER_DISTANCE = 2; // Distance >= 2 means at least 1 empty tile between

        public static bool CanPlaceOutpostAt(int tileId, Faction faction, out string reason)
        {
            reason = string.Empty;
            if (Find.WorldGrid == null || faction == null) return true;

            WorldGrid grid = Find.WorldGrid;
            var allObjects = Find.WorldObjects.AllWorldObjects;

            // 0.7: classification is mod-agnostic — see Integration.WorldObjectClassifier.
            var settlements = allObjects
                .Where(Integration.WorldObjectClassifier.IsSettlement)
                .ToList();

            var allOutposts = allObjects
                .Where(Integration.WorldObjectClassifier.IsOutpost)
                .ToList();

            // 1. Buffer Check: At least 1 tile between outposts and settlements (Distance >= 2)
            foreach (var s in settlements)
            {
                int dist = grid.TraversalDistanceBetween(tileId, s.Tile);
                if (dist < MIN_BUFFER_DISTANCE)
                {
                    reason = "Cannot build outpost here: Must have at least 1 tile between outposts and settlements.";
                    return false;
                }
            }

            foreach (var op in allOutposts)
            {
                int dist = grid.TraversalDistanceBetween(tileId, op.Tile);
                if (dist < MIN_BUFFER_DISTANCE)
                {
                    reason = "Cannot build outpost here: Must have at least 1 tile between outposts and other outposts.";
                    return false;
                }
            }

            // 2. Maximum Supply Range Check (<= 8 tiles from territory border or faction outpost)
            var factionOutposts = allOutposts.Where(o => o.Faction == faction).ToList();
            int minOutpostDist = int.MaxValue;
            foreach (var op in factionOutposts)
            {
                int dist = grid.TraversalDistanceBetween(tileId, op.Tile);
                if (dist < minOutpostDist) minOutpostDist = dist;
            }

            int minBorderDist = GetDistanceToFactionBorder(tileId, faction, grid);
            int minSupplyDist = Mathf.Min(minOutpostDist, minBorderDist);

            if (minSupplyDist > MAX_SUPPLY_DISTANCE)
            {
                reason = $"Too far from your territory border or outposts (must be within {MAX_SUPPLY_DISTANCE} tiles).";
                return false;
            }

            return true;
        }

        private static int GetDistanceToFactionBorder(int tileId, Faction faction, WorldGrid grid)
        {
            var regionManager = Find.World?.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return int.MaxValue;

            int minDist = int.MaxValue;
            string fid = faction.GetUniqueLoadID();

            foreach (var province in regionManager.Provinces)
            {
                var data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);
                bool holdsTerritory = province.owningFactionIds.Contains(fid) || 
                                     (data != null && data.factionScores.Any(s => s.faction == faction && s.TotalScore >= 0.30f));

                if (holdsTerritory)
                {
                    var borderTiles = RegionalOwnershipUtility.GetPerimeterTiles(province);
                    if (borderTiles.Count == 0) borderTiles = new HashSet<int>(province.tiles);

                    foreach (int bTile in borderTiles)
                    {
                        int dist = grid.TraversalDistanceBetween(tileId, bTile);
                        if (dist < minDist) minDist = dist;
                    }
                }
            }
            return minDist;
        }
    }
}
