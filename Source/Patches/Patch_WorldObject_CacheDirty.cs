using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace RimSynapse.RegionsAndTerritories.Patches
{
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.PostAdd))]
    public static class Patch_WorldObject_PostAdd
    {
        [HarmonyPostfix]
        public static void Postfix(WorldObject __instance)
        {
            // 0.7: any object that contributes residents invalidates the density cache,
            // not just vanilla settlements and VOE outposts.
            if (Integration.WorldObjectClassifier.HasPopulation(__instance))
            {
                PopulationDensityUtility.MarkCacheDirty();
            }
        }
    }

    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.PostRemove))]
    public static class Patch_WorldObject_PostRemove
    {
        [HarmonyPostfix]
        public static void Postfix(WorldObject __instance)
        {
            // 0.7: any object that contributes residents invalidates the density cache,
            // not just vanilla settlements and VOE outposts.
            if (Integration.WorldObjectClassifier.HasPopulation(__instance))
            {
                PopulationDensityUtility.MarkCacheDirty();
            }
        }
    }
}
