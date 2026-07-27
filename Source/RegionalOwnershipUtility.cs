using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimSynapse.RegionsAndTerritories
{
    public class FactionOwnershipScore
    {
        public Faction faction;
        public float settlementScore;
        public float perimeterCoverageScore;
        public float externalPerimeterScore;
        public float outpostCoverageScore;
        public float mostOutpostsScore;
        public float demographicScore;

        public float TotalScore => Mathf.Clamp01(settlementScore + perimeterCoverageScore + externalPerimeterScore + outpostCoverageScore + mostOutpostsScore + demographicScore);
    }

    public class RegionalOwnershipData
    {
        public GeographicProvince province;
        public List<FactionOwnershipScore> factionScores = new List<FactionOwnershipScore>();
        public float unclaimedScore = 1f;

        public Faction PrimaryOwner => factionScores.OrderByDescending(s => s.TotalScore).FirstOrDefault(s => s.TotalScore > 0f)?.faction;
    }

    public static class RegionalOwnershipUtility
    {
        public static RegionalOwnershipData CalculateOwnership(GeographicProvince province)
        {
            var data = new RegionalOwnershipData { province = province };
            if (province == null || province.tiles == null || province.tiles.Count == 0 || Find.WorldGrid == null)
            {
                return data;
            }

            var allFactions = Find.FactionManager.AllFactionsListForReading;
            var regionObjects = Find.WorldObjects.AllWorldObjects.Where(obj => province.tiles.Contains(obj.Tile)).ToList();

            // 0.7: classification is mod-agnostic — see Integration.WorldObjectClassifier.
            var settlements = regionObjects.Where(Integration.WorldObjectClassifier.IsSettlement).ToList();
            var outposts = regionObjects.Where(Integration.WorldObjectClassifier.IsOutpost).ToList();

            HashSet<Faction> candidateFactions = GetCandidateFactions(settlements, outposts, allFactions);
            if (candidateFactions.Count == 0)
            {
                return data;
            }

            HashSet<int> perimeterTiles = GetPerimeterTiles(province);
            Dictionary<int, Faction> perimeterOwnerMap = MapPerimeterTileOwners(perimeterTiles, settlements, outposts);

            CalculateFactionScores(data, candidateFactions, province, settlements, outposts, perimeterTiles, perimeterOwnerMap);

            float assignedTotal = data.factionScores.Sum(s => s.TotalScore);
            data.unclaimedScore = Mathf.Max(0f, 1f - assignedTotal);

            return data;
        }

        private static HashSet<Faction> GetCandidateFactions(List<WorldObject> settlements, List<WorldObject> outposts, List<Faction> allFactions)
        {
            HashSet<Faction> candidates = new HashSet<Faction>();
            foreach (var s in settlements)
            {
                if (s.Faction != null) candidates.Add(s.Faction);
            }
            foreach (var o in outposts)
            {
                if (o.Faction != null) candidates.Add(o.Faction);
            }
            return candidates;
        }

        public static HashSet<int> GetPerimeterTiles(GeographicProvince province)
        {
            HashSet<int> provinceTileSet = new HashSet<int>(province.tiles);
            HashSet<int> perimeter = new HashSet<int>();
            WorldGrid grid = Find.WorldGrid;

            List<PlanetTile> neighbors = new List<PlanetTile>();
            foreach (int tileId in province.tiles)
            {
                grid.GetTileNeighbors(tileId, neighbors);
                foreach (var n in neighbors)
                {
                    if (!provinceTileSet.Contains(n.tileId))
                    {
                        perimeter.Add(tileId);
                        break;
                    }
                }
            }
            return perimeter;
        }

        private static Dictionary<int, Faction> MapPerimeterTileOwners(HashSet<int> perimeterTiles, List<WorldObject> settlements, List<WorldObject> outposts)
        {
            Dictionary<int, Faction> map = new Dictionary<int, Faction>();
            WorldGrid grid = Find.WorldGrid;

            var activeObjects = settlements.Concat(outposts).Where(o => o.Faction != null).ToList();
            if (activeObjects.Count == 0) return map;

            foreach (int tileId in perimeterTiles)
            {
                WorldObject closestObj = null;
                int minDist = int.MaxValue;

                foreach (var obj in activeObjects)
                {
                    int dist = grid.TraversalDistanceBetween(tileId, obj.Tile);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestObj = obj;
                    }
                }

                if (closestObj != null && closestObj.Faction != null)
                {
                    map[tileId] = closestObj.Faction;
                }
            }
            return map;
        }

        private static void CalculateFactionScores(RegionalOwnershipData data, HashSet<Faction> factions, GeographicProvince province, List<WorldObject> settlements, List<WorldObject> outposts, HashSet<int> perimeterTiles, Dictionary<int, Faction> perimeterOwnerMap)
        {
            Faction maxExternalOwner = GetMaxExternalPerimeterOwner(perimeterOwnerMap);
            Faction maxOutpostOwner = GetMaxOutpostOwner(outposts);

            foreach (Faction f in factions)
            {
                var score = new FactionOwnershipScore { faction = f };

                // 1. Settlement Score (20%)
                if (settlements.Count > 0)
                {
                    int fSettlements = settlements.Count(s => s.Faction == f);
                    score.settlementScore = 0.20f * ((float)fSettlements / settlements.Count);
                }

                // 2. Perimeter Coverage Score (20%) and External Perimeter Bonus (10%)
                if (perimeterTiles.Count > 0)
                {
                    int fPerimeterCount = perimeterOwnerMap.Values.Count(v => v == f);
                    score.perimeterCoverageScore = 0.20f * ((float)fPerimeterCount / perimeterTiles.Count);
                }
                if (maxExternalOwner != null && f == maxExternalOwner)
                {
                    score.externalPerimeterScore = 0.10f;
                }

                // 3. Outpost Coverage Score (20%) and Most Outposts Bonus (10%)
                if (outposts.Count > 0)
                {
                    int fOutpostCount = outposts.Count(o => o.Faction == f);
                    score.outpostCoverageScore = 0.20f * ((float)fOutpostCount / outposts.Count);
                }
                if (maxOutpostOwner != null && f == maxOutpostOwner)
                {
                    score.mostOutpostsScore = 0.10f;
                }

                // 4. Demographic Score (20%)
                score.demographicScore = CalculateDemographicScore(province, f, settlements);

                data.factionScores.Add(score);
            }
        }

        private static Faction GetMaxExternalPerimeterOwner(Dictionary<int, Faction> perimeterOwnerMap)
        {
            if (perimeterOwnerMap.Count == 0) return null;
            var groups = perimeterOwnerMap.Values.GroupBy(v => v).OrderByDescending(g => g.Count()).ToList();
            if (groups.Count > 0 && groups[0].Count() > 0)
            {
                return groups[0].Key;
            }
            return null;
        }

        private static Faction GetMaxOutpostOwner(List<WorldObject> outposts)
        {
            var validOutposts = outposts.Where(o => o.Faction != null).ToList();
            if (validOutposts.Count == 0) return null;
            var groups = validOutposts.GroupBy(o => o.Faction).OrderByDescending(g => g.Count()).ToList();
            if (groups.Count > 0 && groups[0].Count() > 0)
            {
                return groups[0].Key;
            }
            return null;
        }

        private static float CalculateDemographicScore(GeographicProvince province, Faction faction, List<WorldObject> settlements)
        {
            if (RegionalDemographicRegistry.HasProviders)
            {
                float demoMatch = RegionalDemographicRegistry.GetCombinedDemographicScore(province, faction);
                if (demoMatch >= 0f)
                {
                    return 0.20f * demoMatch;
                }
            }

            // Non-DLC fallback: if faction has settlement presence, return full 20%
            if (settlements.Any(s => s.Faction == faction))
            {
                return 0.20f;
            }
            return 0f;
        }
    }
}
