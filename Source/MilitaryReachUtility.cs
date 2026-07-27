using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Military;
using RimSynapse.RegionsAndTerritories.Placement;
using Verse;

namespace RimSynapse.RegionsAndTerritories
{
    /// <summary>
    /// The one place military reach touches the world. Same shape as
    /// <c>WorldObjectPlacementUtility</c>, <c>SettlementSizeUtility</c>,
    /// <c>ProductionScalingUtility</c> and <c>TaxationUtility</c>: reads <c>Find</c>, photographs
    /// what it finds into a <see cref="SupplyNetwork"/>, decides nothing.
    ///
    /// <para>Epic 5 children 1 and 2. Child 1 asked for Empire's adjacency restriction to be
    /// extracted into a reusable service applied to <i>any</i> mod's military action; this is that
    /// service, and it names no mod. The Empire prefix that used to carry the rule inline now asks
    /// here, exactly as its production postfix now asks <c>ProductionScalingUtility</c>.</para>
    ///
    /// <para>It also closes a real gap rather than only refactoring one: the
    /// <c>militaryGovernance</c> setting has existed in the settings dialog since Epic 1 and until
    /// now controlled nothing at all, because the adjacency check never consulted it. A switch that
    /// does nothing is worse than a missing switch.</para>
    /// </summary>
    public static class MilitaryReachUtility
    {
        // The province adjacency map is expensive to derive (SynapseRegionManager.AreProvincesAdjacent
        // is a tile-by-tile comparison of two provinces) and never changes after worldgen, so it is
        // built once per world and held. Keyed on the world instance rather than on a bool, because
        // the failure this guards against is a second save loading into a stale map — a bug that
        // would present as military reach obeying the previous game's geography.
        private static object cachedWorld;
        private static Dictionary<int, List<int>> cachedAdjacency;

        /// <summary>
        /// Can <paramref name="faction"/> reach <paramref name="targetTileId"/> from
        /// <paramref name="sourceTileId"/>, and in what state does the force arrive?
        ///
        /// Returns an unrestricted line whenever governance is off or the world has no province data
        /// — a military hook that refuses an action because it could not find the world is far worse
        /// than one that stands aside.
        /// </summary>
        public static SupplyLine ReachBetweenTiles(int sourceTileId, int targetTileId, Faction faction)
        {
            if (!WorldObjectIntegrationSettings.MilitaryGovernanceActive) return SupplyLine.Unrestricted();
            if (sourceTileId < 0 || targetTileId < 0 || Find.World == null) return SupplyLine.Unrestricted();

            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return SupplyLine.Unrestricted();

            int sourceProvinceId = regionManager.GetProvinceId(sourceTileId);
            int targetProvinceId = regionManager.GetProvinceId(targetTileId);
            if (sourceProvinceId < 0 || targetProvinceId < 0) return SupplyLine.Unrestricted();

            SupplyNetwork network = BuildNetwork(regionManager);
            if (network == null) return SupplyLine.Unrestricted();

            return SupplyEvaluator.Evaluate(network, sourceProvinceId, targetProvinceId, faction);
        }

        /// <summary>Yes/no plus a message, for call sites that only want to allow or refuse.</summary>
        public static bool CanReach(int sourceTileId, int targetTileId, Faction faction, out string reason)
        {
            SupplyLine line = ReachBetweenTiles(sourceTileId, targetTileId, faction);
            reason = line.Reason ?? string.Empty;
            return line.Reachable;
        }

        public static SupplyNetwork BuildNetwork(SynapseRegionManager regionManager)
        {
            if (regionManager == null) return null;

            Dictionary<int, List<int>> adjacency = Adjacency(regionManager);

            return new SupplyNetwork
            {
                Neighbours = provinceId => adjacency.ContainsKey(provinceId)
                    ? (IEnumerable<int>)adjacency[provinceId]
                    : new int[0],
                ControlOf = (provinceId, faction) => ControlOf(regionManager, provinceId, faction as Faction)
            };
        }

        private static ProvinceControl ControlOf(SynapseRegionManager regionManager, int provinceId, Faction faction)
        {
            if (regionManager == null || provinceId < 0 || faction == null) return ProvinceControl.Unclaimed;

            GeographicProvince province = regionManager.GetProvince(provinceId);
            if (province == null) return ProvinceControl.Unclaimed;

            // The same question the placement layer asks, answered by the same code. Supply and
            // placement disagreeing about who holds a province would be a bug nobody could reproduce.
            return RegionalOwnershipUtility.GetControl(province, faction);
        }

        /// <summary>
        /// Province adjacency for the whole map, derived once by walking tiles rather than by
        /// comparing every province against every other. Building it the direct way is
        /// O(provinces² × tiles²); walking each tile's own neighbours and recording which province
        /// each lands in is O(tiles), which is the difference between a lazy cache and a stutter.
        /// </summary>
        private static Dictionary<int, List<int>> Adjacency(SynapseRegionManager regionManager)
        {
            object world = Find.World;
            if (cachedAdjacency != null && ReferenceEquals(cachedWorld, world)) return cachedAdjacency;

            var adjacency = new Dictionary<int, List<int>>();
            WorldGrid grid = Find.WorldGrid;

            foreach (GeographicProvince province in regionManager.Provinces)
            {
                if (province == null) continue;
                if (!adjacency.ContainsKey(province.id)) adjacency[province.id] = new List<int>();
            }

            if (grid != null)
            {
                var neighbourTiles = new List<PlanetTile>();

                foreach (GeographicProvince province in regionManager.Provinces)
                {
                    if (province == null || province.tiles == null) continue;

                    List<int> edges = adjacency[province.id];

                    foreach (int tile in province.tiles)
                    {
                        neighbourTiles.Clear();
                        grid.GetTileNeighbors(tile, neighbourTiles);

                        foreach (PlanetTile neighbour in neighbourTiles)
                        {
                            int other = regionManager.GetProvinceId(neighbour);
                            if (other < 0 || other == province.id) continue;
                            if (!edges.Contains(other)) edges.Add(other);
                        }
                    }
                }
            }

            cachedWorld = world;
            cachedAdjacency = adjacency;
            return adjacency;
        }

        /// <summary>Drop the cached map. Called when a world is discarded or provinces regenerate.</summary>
        public static void ClearCache()
        {
            cachedWorld = null;
            cachedAdjacency = null;
        }
    }
}
