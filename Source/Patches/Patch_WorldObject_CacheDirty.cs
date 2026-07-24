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
            if (__instance is Settlement || PopulationDensityUtility.IsVoeOutpost(__instance))
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
            if (__instance is Settlement || PopulationDensityUtility.IsVoeOutpost(__instance))
            {
                PopulationDensityUtility.MarkCacheDirty();
            }
        }
    }
}
