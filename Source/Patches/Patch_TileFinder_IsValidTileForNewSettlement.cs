using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimSynapse.RegionsAndTerritories.Patches
{
    [HarmonyPatch(typeof(TileFinder), nameof(TileFinder.IsValidTileForNewSettlement))]
    public static class Patch_TileFinder_IsValidTileForNewSettlement
    {
        [HarmonyPostfix]
        public static void Postfix(PlanetTile tile, ref bool __result, StringBuilder reason)
        {
            // If it's already invalid, do nothing
            if (!__result) return;
            if (tile == null) return;

            if (Find.World == null) return;

            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return;

            int tileId = tile.tileId;
            int provinceId = regionManager.GetProvinceId(tileId);
            if (provinceId == -1) return;

            var province = regionManager.Provinces.FirstOrDefault(p => p.id == provinceId);
            if (province == null) return;

            // 1. Found a claimed region, block settling!
            if (province.owningFactionIds.Any())
            {
                __result = false;
                reason?.AppendLine("Cannot settle here: This region is claimed by another faction.");
                return;
            }

            // 2. Sequential Expansion constraint: must be adjacent to existing territory if we have one
            var playerBases = Find.WorldObjects.AllWorldObjects
                .Where(obj => obj.Faction != null && (obj.Faction.IsPlayer || obj.GetType().Name == "WorldSettlementFC"))
                .ToList();

            if (playerBases.Any())
            {
                bool hasFoothold = false;
                foreach (var pb in playerBases)
                {
                    int pbProvinceId = regionManager.GetProvinceId(pb.Tile);
                    if (pbProvinceId == provinceId)
                    {
                        hasFoothold = true;
                        break;
                    }
                    var pbProvince = regionManager.Provinces.FirstOrDefault(p => p.id == pbProvinceId);
                    if (pbProvince != null && regionManager.AreProvincesAdjacent(pbProvince, province))
                    {
                        hasFoothold = true;
                        break;
                    }
                }

                if (!hasFoothold)
                {
                    __result = false;
                    reason?.AppendLine("Cannot settle here: This region is too far from your existing territory. You must expand to adjacent regions first.");
                    return;
                }
            }
        }
    }
}
