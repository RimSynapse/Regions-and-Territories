using HarmonyLib;
using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimSynapse.RegionsAndTerritories.Patches
{
    /// <summary>
    /// Modifier + click a region (in a region map mode) to open its draggable expanded readout (#53).
    /// Ctrl+click or Shift+click depending on <see cref="FactionPlacementSettings.regionPanelUseShift"/>
    /// (set in the mod menu so it can be moved off a conflicting key). Each opens a window pinned to that
    /// region, so several can be open at once to compare regions. A plain click keeps the normal
    /// select/highlight behaviour.
    /// </summary>
    [HarmonyPatch(typeof(WorldSelector), "SelectUnderMouse")]
    public static class Patch_WorldSelector_RegionPanelClick
    {
        private static bool InOverrideRegionMode()
        {
            var current = MapModeComponent.Instance?.currentMapMode as MapMode_Region;
            return current != null && current.def?.RegionProperties?.overrideSelector == true;
        }

        private static bool ModifierHeld()
        {
            Event e = Event.current;
            if (e == null) return false;
            return FactionPlacementSettings.regionPanelUseShift ? e.shift : e.control;
        }

        public static void Postfix()
        {
            if (!InOverrideRegionMode() || !ModifierHeld())
            {
                return;
            }
            int tile = GenWorld.MouseTile();
            if (tile < 0)
            {
                return;
            }
            var mgr = Find.World?.GetComponent<SynapseRegionManager>();
            var province = mgr?.GetProvinceForTile(tile);
            UI.RegionInfoWindow.OpenFor(province, tile);
        }
    }
}
