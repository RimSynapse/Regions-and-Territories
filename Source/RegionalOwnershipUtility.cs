using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Placement;
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

        /// <summary>This faction's share of the province, 0 if it has no presence at all.</summary>
        public float ScoreFor(Faction faction)
        {
            if (faction == null) return 0f;
            var entry = factionScores.FirstOrDefault(s => s.faction == faction);
            return entry != null ? entry.TotalScore : 0f;
        }

        /// <summary>
        /// The highest score held by anyone other than <paramref name="faction"/>, 0 if it has the
        /// province to itself.
        ///
        /// The faction's own entry is skipped explicitly. Holding a province harder must never read
        /// as more pressure on yourself — that inversion is the obvious way to get every consumer of
        /// this number backwards at once, and there are now two of them: Epic 3's derived security
        /// and Epic 3 child 6's interception. Null and factionless entries are stepped over rather
        /// than thrown on, because ownership data is rebuilt from live world objects and a holding
        /// can lose its faction between one rebuild and the next.
        ///
        /// The strongest rival, not the sum of them: three weak neighbours are not equivalent to one
        /// strong one, and summing would make a crowded map uniformly hostile.
        /// </summary>
        public float StrongestRivalScore(Faction faction)
        {
            if (factionScores == null) return 0f;

            float strongest = 0f;
            foreach (var score in factionScores)
            {
                if (score == null || score.faction == null) continue;
                if (score.faction == faction) continue;
                if (score.TotalScore > strongest) strongest = score.TotalScore;
            }

            return strongest;
        }

        /// <summary>Every faction scoring above the ownership threshold, strongest first.</summary>
        public List<FactionOwnershipScore> Contenders()
        {
            return factionScores
                .Where(s => s.TotalScore >= PlacementRules.OwnershipThreshold)
                .OrderByDescending(s => s.TotalScore)
                .ToList();
        }

        /// <summary>
        /// True when the two strongest factions both clear the threshold and are within
        /// <see cref="PlacementRules.ContestMargin"/> of each other. A contested province has no
        /// settled owner and placement rules treat it differently from foreign ground.
        /// </summary>
        public bool IsContested()
        {
            var contenders = Contenders();
            if (contenders.Count < 2) return false;
            return contenders[0].TotalScore - contenders[1].TotalScore <= PlacementRules.ContestMargin;
        }
    }

    public static class RegionalOwnershipUtility
    {
        // 0.7: how much each kind of holding counts toward its faction's claim.
        //
        // Before 0.7 only settlements and outposts scored at all — a faction could garrison a
        // province with military installations and forward camps and still read as having no
        // presence there. The weights below are chosen so a world containing only settlements and
        // outposts scores exactly as it did before; the new kinds add to the picture rather than
        // redistributing it.
        private const float SettlementWeight = 1.0f;
        private const float MilitaryWeight = 0.6f;
        private const float OutpostWeight = 1.0f;
        private const float CampWeight = 0.4f;

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
            // Primary holdings are the population centres and the forces stationed to hold them;
            // secondary holdings are the production and forward positions that support them.
            var primary = regionObjects.Where(o => IsKind(o, WorldObjectKind.Settlement, WorldObjectKind.Military)).ToList();
            var secondary = regionObjects.Where(o => IsKind(o, WorldObjectKind.Outpost, WorldObjectKind.Camp)).ToList();

            HashSet<Faction> candidateFactions = GetCandidateFactions(primary, secondary);
            if (candidateFactions.Count == 0)
            {
                return data;
            }

            HashSet<int> perimeterTiles = GetPerimeterTiles(province);
            Dictionary<int, Faction> perimeterOwnerMap = MapPerimeterTileOwners(perimeterTiles, primary, secondary);

            CalculateFactionScores(data, candidateFactions, province, primary, secondary, perimeterTiles, perimeterOwnerMap);

            float assignedTotal = data.factionScores.Sum(s => s.TotalScore);
            data.unclaimedScore = Mathf.Max(0f, 1f - assignedTotal);

            return data;
        }

        /// <summary>
        /// How <paramref name="faction"/> stands in <paramref name="province"/>. This is the single
        /// answer placement, expansion, and the inspect pane all read, so they can never disagree.
        /// </summary>
        public static ProvinceControl GetControl(GeographicProvince province, Faction faction)
        {
            if (province == null || faction == null) return ProvinceControl.Unclaimed;

            string fid = faction.GetUniqueLoadID();
            var data = province.ownershipData ?? CalculateOwnership(province);

            bool listedAsOwner = province.owningFactionIds != null && province.owningFactionIds.Contains(fid);
            bool someoneElseListed = province.owningFactionIds != null
                && province.owningFactionIds.Any(id => !string.Equals(id, fid, StringComparison.Ordinal));

            if (data == null)
            {
                if (listedAsOwner) return ProvinceControl.Held;
                return someoneElseListed ? ProvinceControl.Foreign : ProvinceControl.Unclaimed;
            }

            var contenders = data.Contenders();
            bool scoresAsOwner = listedAsOwner || data.ScoreFor(faction) >= PlacementRules.OwnershipThreshold;

            if (scoresAsOwner)
            {
                return data.IsContested() && contenders.Any(c => c.faction != faction)
                    ? ProvinceControl.Contested
                    : ProvinceControl.Held;
            }

            if (contenders.Count > 0 || someoneElseListed) return ProvinceControl.Foreign;

            return ProvinceControl.Unclaimed;
        }

        /// <summary>True when the faction holds or co-holds the province — the old inline 0.30f test.</summary>
        public static bool HoldsTerritory(GeographicProvince province, Faction faction)
        {
            ProvinceControl control = GetControl(province, faction);
            return control == ProvinceControl.Held || control == ProvinceControl.Contested;
        }

        private static bool IsKind(WorldObject obj, WorldObjectKind a, WorldObjectKind b)
        {
            WorldObjectKind kind = WorldObjectClassifier.Classify(obj);
            return kind == a || kind == b;
        }

        private static float WeightOf(WorldObject obj)
        {
            switch (WorldObjectClassifier.Classify(obj))
            {
                case WorldObjectKind.Settlement: return SettlementWeight;
                case WorldObjectKind.Military: return MilitaryWeight;
                case WorldObjectKind.Outpost: return OutpostWeight;
                case WorldObjectKind.Camp: return CampWeight;
                default: return 0f;
            }
        }

        private static float WeightedTotal(List<WorldObject> objects)
        {
            float total = 0f;
            foreach (var o in objects) total += WeightOf(o);
            return total;
        }

        private static float WeightedTotalFor(List<WorldObject> objects, Faction faction)
        {
            float total = 0f;
            foreach (var o in objects)
            {
                if (o.Faction == faction) total += WeightOf(o);
            }
            return total;
        }

        private static HashSet<Faction> GetCandidateFactions(List<WorldObject> primary, List<WorldObject> secondary)
        {
            HashSet<Faction> candidates = new HashSet<Faction>();
            foreach (var s in primary)
            {
                if (s.Faction != null) candidates.Add(s.Faction);
            }
            foreach (var o in secondary)
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

        private static Dictionary<int, Faction> MapPerimeterTileOwners(HashSet<int> perimeterTiles, List<WorldObject> primary, List<WorldObject> secondary)
        {
            Dictionary<int, Faction> map = new Dictionary<int, Faction>();
            WorldGrid grid = Find.WorldGrid;

            var activeObjects = primary.Concat(secondary).Where(o => o.Faction != null).ToList();
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

        private static void CalculateFactionScores(RegionalOwnershipData data, HashSet<Faction> factions, GeographicProvince province, List<WorldObject> primary, List<WorldObject> secondary, HashSet<int> perimeterTiles, Dictionary<int, Faction> perimeterOwnerMap)
        {
            Faction maxExternalOwner = GetMaxExternalPerimeterOwner(perimeterOwnerMap);
            Faction maxSecondaryOwner = GetMaxSecondaryHoldingOwner(secondary);

            float primaryTotal = WeightedTotal(primary);
            float secondaryTotal = WeightedTotal(secondary);

            foreach (Faction f in factions)
            {
                var score = new FactionOwnershipScore { faction = f };

                // 1. Primary holding share (20%) — settlements, plus military installations at
                //    reduced weight. Identical to the pre-0.7 settlement share when no military
                //    installations are present.
                if (primaryTotal > 0f)
                {
                    score.settlementScore = 0.20f * (WeightedTotalFor(primary, f) / primaryTotal);
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

                // 3. Secondary holding share (20%) and most-holdings bonus (10%) — outposts, plus
                //    camps at reduced weight.
                if (secondaryTotal > 0f)
                {
                    score.outpostCoverageScore = 0.20f * (WeightedTotalFor(secondary, f) / secondaryTotal);
                }
                if (maxSecondaryOwner != null && f == maxSecondaryOwner)
                {
                    score.mostOutpostsScore = 0.10f;
                }

                // 4. Demographic Score (20%)
                score.demographicScore = CalculateDemographicScore(province, f, primary);

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

        private static Faction GetMaxSecondaryHoldingOwner(List<WorldObject> secondary)
        {
            var valid = secondary.Where(o => o.Faction != null).ToList();
            if (valid.Count == 0) return null;

            var groups = valid
                .GroupBy(o => o.Faction)
                .Select(g => new { faction = g.Key, weight = g.Sum(WeightOf) })
                .OrderByDescending(g => g.weight)
                .ToList();

            return groups.Count > 0 && groups[0].weight > 0f ? groups[0].faction : null;
        }

        /// <summary>
        /// Contributes nothing in 0.7, deliberately. Do not "fix" this back to a value.
        ///
        /// <para>This component is supposed to express what share of a region's people are a
        /// given faction's. It never did. The provider path was real —
        /// <see cref="RegionalDemographicRegistry"/> was consulted and Factions registers an
        /// ideology provider into it — but underneath sat a fallback returning the full 20%
        /// for merely owning a primary holding in the region, which <c>settlementScore</c>
        /// already measures. The same fact was counted twice, the second time under a name
        /// implying something else entirely.</para>
        ///
        /// <para>That fallback was not an edge case. It fired on every install where the
        /// provider path yielded nothing — no providers registered, Ideology inactive, or a
        /// provider returning a negative — which is most of them. It is **deleted** rather
        /// than left dormant behind a zero, because a path that silently double-counts is
        /// exactly what someone later switches back on while "fixing" an unexplained 0.</para>
        ///
        /// <para>The registry, provider registration and Factions' own provider are all left
        /// wired, so 0.8 inherits a live surface rather than rebuilding one. 0.8 replaces this
        /// with a read of the regional ideological distribution (Regions-and-Territories#34),
        /// and Regions-and-Territories#44 makes the component's availability explicit so an
        /// unavailable one leaves the denominator instead of quietly lowering every score.
        /// Until then, ownership is scored only on what 0.7 actually models.</para>
        ///
        /// <para>Parameters are retained so the signature does not churn when 0.8 restores the
        /// body.</para>
        /// </summary>
        private static float CalculateDemographicScore(GeographicProvince province, Faction faction, List<WorldObject> primary)
        {
            return 0f;
        }
    }
}
