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

        /// <summary>
        /// Rescale every component by the same factor. Used to normalise over the components that
        /// are actually in play, so the displayed breakdown and the total stay consistent (#44).
        /// </summary>
        public void Scale(float factor)
        {
            settlementScore *= factor;
            perimeterCoverageScore *= factor;
            externalPerimeterScore *= factor;
            outpostCoverageScore *= factor;
            mostOutpostsScore *= factor;
            demographicScore *= factor;
        }
    }

    public class RegionalOwnershipData
    {
        public GeographicProvince province;
        public List<FactionOwnershipScore> factionScores = new List<FactionOwnershipScore>();
        public float unclaimedScore = 1f;

        /// <summary>Dev-mode only: the raw pre-normalization derivation, shown in the tooltip so the
        /// scoring can be tuned against ground truth rather than reverse-engineered.</summary>
        public string debugBreakdown;

        // Diagnostics captured during scoring, for the dev derivation block.
        public int primaryCount;
        public int secondaryCount;
        public int claimedBorderEdges;
        public int totalBorderEdges;

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
            // Fallback for callers without pre-bucketed objects (e.g. GetControl on one province).
            // RecalculateProvinceOwners buckets every world object once, O(worldObjects), and calls
            // the overload below — replacing the per-province AllWorldObjects.Where(tiles.Contains)
            // scan that was O(worldObjects * tiles) summed across the map (#48).
            List<WorldObject> regionObjects = (province?.tiles != null && Find.WorldObjects != null)
                ? Find.WorldObjects.AllWorldObjects.Where(obj => province.tiles.Contains(obj.Tile)).ToList()
                : new List<WorldObject>();
            return CalculateOwnership(province, regionObjects);
        }

        public static RegionalOwnershipData CalculateOwnership(GeographicProvince province, List<WorldObject> regionObjects)
        {
            // Single-province fallback (e.g. GetControl with no cached data): base scores + normalize,
            // WITHOUT border influence — that needs the neighbour owners the two-pass
            // RecalculateProvinceOwners supplies. The cached ownershipData it writes DOES include borders.
            var data = CalculateOwnershipBase(province, regionObjects);
            NormalizeAndCapture(data, province, null);
            return data;
        }

        /// <summary>
        /// Pass 1: a province's ownership from its own holdings only — settlements, outposts,
        /// demographics — with no border influence and no normalization. The dominant owner of this
        /// is what neighbouring provinces read when computing their border scores.
        /// </summary>
        public static RegionalOwnershipData CalculateOwnershipBase(GeographicProvince province, List<WorldObject> regionObjects)
        {
            var data = new RegionalOwnershipData { province = province };
            if (province == null || province.tiles == null || province.tiles.Count == 0 || Find.WorldGrid == null)
            {
                return data;
            }
            if (regionObjects == null) regionObjects = new List<WorldObject>();

            // 0.7: classification is mod-agnostic — see Integration.WorldObjectClassifier.
            // Primary holdings are the population centres and the forces stationed to hold them;
            // secondary holdings are the production and forward positions that support them.
            var primary = regionObjects.Where(o => IsKind(o, WorldObjectKind.Settlement, WorldObjectKind.Military)).ToList();
            var secondary = regionObjects.Where(o => IsKind(o, WorldObjectKind.Outpost, WorldObjectKind.Camp)).ToList();
            data.primaryCount = primary.Count;
            data.secondaryCount = secondary.Count;

            HashSet<Faction> candidateFactions = GetCandidateFactions(primary, secondary);
            if (candidateFactions.Count == 0) return data;   // no holdings: only neighbours' borders can claim it

            Faction maxSecondaryOwner = GetMaxSecondaryHoldingOwner(secondary);
            float primaryTotal = WeightedTotal(primary);
            float secondaryTotal = WeightedTotal(secondary);

            foreach (Faction f in candidateFactions)
            {
                var score = new FactionOwnershipScore { faction = f };
                if (primaryTotal > 0f)  score.settlementScore = 0.20f * (WeightedTotalFor(primary, f) / primaryTotal);
                if (secondaryTotal > 0f) score.outpostCoverageScore = 0.20f * (WeightedTotalFor(secondary, f) / secondaryTotal);
                if (maxSecondaryOwner != null && f == maxSecondaryOwner) score.mostOutpostsScore = 0.10f;
                score.demographicScore = CalculateDemographicScore(province, f, primary);
                data.factionScores.Add(score);
            }
            return data;
        }

        /// <summary>The strongest holder of a province by its own holdings (pass-1 base score), or
        /// null if nobody clears a small floor. Neighbours read this for their border scores.</summary>
        public static Faction DominantBaseOwner(RegionalOwnershipData data)
        {
            if (data == null) return null;
            FactionOwnershipScore best = null;
            foreach (var s in data.factionScores)
                if (s.faction != null && (best == null || s.TotalScore > best.TotalScore)) best = s;
            return (best != null && best.TotalScore > 0.05f) ? best.faction : null;
        }

        // Border is a single 0.30 component, distributed by neighbour ownership; the old split of
        // perimeter-coverage (0.20) + external-perimeter (0.10) is gone with the tile-nearest-object
        // mapping it came from. perimeterCoverageScore carries it now; externalPerimeterScore stays 0.
        private const float BorderWeight = 0.30f;

        /// <summary>
        /// Pass 2: add border influence from neighbouring provinces, then normalize. A neighbour's
        /// owner claims this province's shared border in proportion to the shared hex-edge count
        /// (precomputed and static in <see cref="GeographicProvince.borderShares"/>). Unclaimed
        /// neighbours contribute nothing; with no claimed neighbour, borders score 0 for everyone.
        /// Because the geometry is static, a neighbour changing owner is a cheap recompute (#44).
        /// </summary>
        public static void ApplyBordersAndNormalize(RegionalOwnershipData data, GeographicProvince province, Dictionary<int, Faction> ownerByProvince)
        {
            if (data == null || province == null) return;

            var edgesByFaction = new Dictionary<Faction, int>();
            int claimedEdges = 0, totalEdges = 0;
            if (province.borderShares != null)
            {
                foreach (var kv in province.borderShares)
                {
                    totalEdges += kv.Value;
                    Faction owner;
                    if (ownerByProvince != null && ownerByProvince.TryGetValue(kv.Key, out owner) && owner != null)
                    {
                        int e; edgesByFaction.TryGetValue(owner, out e);
                        edgesByFaction[owner] = e + kv.Value;
                        claimedEdges += kv.Value;
                    }
                }
            }
            data.claimedBorderEdges = claimedEdges;
            data.totalBorderEdges = totalEdges;

            if (claimedEdges > 0 && totalEdges > 0)
            {
                foreach (var kv in edgesByFaction)
                {
                    // Divide by TOTAL land-neighbour edges, not claimed: 80% of the border being one
                    // faction earns it 80% of the border weight, and the unclaimed remainder stays a
                    // wild frontier rather than being handed to the lone claimant (#44). Water / map
                    // edges are already excluded — borderShares only holds edges to other provinces.
                    float borderScore = BorderWeight * ((float)kv.Value / totalEdges);
                    var entry = data.factionScores.FirstOrDefault(s => s.faction == kv.Key);
                    if (entry == null)
                    {
                        entry = new FactionOwnershipScore { faction = kv.Key };  // a neighbour with no holding here still borders it
                        data.factionScores.Add(entry);
                    }
                    entry.perimeterCoverageScore = borderScore;   // the neighbour-weighted border score
                }
            }

            NormalizeAndCapture(data, province, edgesByFaction);
        }

        private static void NormalizeAndCapture(RegionalOwnershipData data, GeographicProvince province, Dictionary<Faction, int> borderEdgesByFaction)
        {
            if (data == null) return;

            // A component's weight belongs in the denominator only if it actually contributed to some
            // faction's score here — not merely because its input exists. A stub that scores nothing
            // (e.g. demographics before #36) must not dilute anyone (#44).
            bool settleInPlay  = data.factionScores.Any(s => s.settlementScore > 0f);
            bool bordersInPlay = data.factionScores.Any(s => s.perimeterCoverageScore + s.externalPerimeterScore > 0f);
            bool outpostInPlay = data.factionScores.Any(s => s.outpostCoverageScore + s.mostOutpostsScore > 0f);
            bool demoInPlay    = data.factionScores.Any(s => s.demographicScore > 0f);
            float inPlayWeight = (settleInPlay ? 0.20f : 0f) + (bordersInPlay ? 0.30f : 0f)
                               + (outpostInPlay ? 0.30f : 0f) + (demoInPlay ? 0.20f : 0f);
            float normScale = (inPlayWeight > 0f && inPlayWeight < 1f) ? 1f / inPlayWeight : 1f;

            if (Prefs.DevMode)
            {
                var dbg = new System.Text.StringBuilder();
                dbg.AppendLine("--- [DEV] raw ownership derivation ---");
                dbg.AppendLine($"primary(settle/mil)={data.primaryCount}  secondary(outpost/camp)={data.secondaryCount}  borderEdges(claimed/total)={data.claimedBorderEdges}/{data.totalBorderEdges}");
                dbg.AppendLine($"inPlay(contributed): settle={settleInPlay}  borders={bordersInPlay}  outposts={outpostInPlay}  demo={demoInPlay}");
                dbg.AppendLine($"inPlayWeight={inPlayWeight:0.00}  scale={normScale:0.00}");
                foreach (var s in data.factionScores)
                {
                    int fEdges = 0;
                    if (borderEdgesByFaction != null) borderEdgesByFaction.TryGetValue(s.faction, out fEdges);
                    dbg.AppendLine($"  {s.faction?.Name}: rawTotal={s.TotalScore:0.000} -> norm={Mathf.Clamp01(s.TotalScore * normScale):0.000}");
                    dbg.AppendLine($"     settle={s.settlementScore:0.000} border={s.perimeterCoverageScore:0.000}({fEdges}/{data.totalBorderEdges} edges) outpost={s.outpostCoverageScore:0.000} most={s.mostOutpostsScore:0.000} demo={s.demographicScore:0.000}");
                }
                data.debugBreakdown = dbg.ToString();
            }

            if (normScale != 1f)
            {
                foreach (var s in data.factionScores) s.Scale(normScale);
            }

            float assignedTotal = data.factionScores.Sum(s => s.TotalScore);
            data.unclaimedScore = Mathf.Max(0f, 1f - assignedTotal);
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
            // Prefer the precomputed perimeter (SynapseRegionManager.BuildProvinceTopology). It is a
            // pure function of tile membership, so rebuilding it per ownership pass was wasted work.
            if (province.perimeterTiles != null) return new HashSet<int>(province.perimeterTiles);

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

        // Border scoring no longer maps perimeter tiles to the nearest object (the
        // TraversalDistanceBetween pass that MapPerimeterTileOwners / GetMaxExternalPerimeterOwner /
        // the old CalculateFactionScores did). It is derived from neighbour ownership over the
        // precomputed borderShares in ApplyBordersAndNormalize (#44) — cheaper and owner-correct.

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
