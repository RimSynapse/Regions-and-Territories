using HarmonyLib;
using MapModeFramework;
using UnityEngine;
using Verse;

namespace RimSynapse.RegionsAndTerritories.Patches
{
    /// <summary>
    /// Adds a "Draw region borders" checkbox to Map Mode Framework's Draw Settings panel (#53), beside
    /// Draw Hills / Rivers / Roads, so the global region-border overlay is toggled from the same place
    /// as the other world overlays and applies on top of any map mode. The panel auto-sizes from a
    /// private height field, so the postfix grows it by one line (reflection) before drawing the row.
    /// </summary>
    [HarmonyPatch(typeof(MapModeUI), "DoDrawSettingsExpanded")]
    public static class Patch_MapModeUI_DrawSettings
    {
        public static void Postfix(MapModeUI __instance, ref Rect inRect)
        {
            try
            {
                Traverse tr = Traverse.Create(__instance);
                Traverse heightField = tr.Field("drawSettingsHeight");
                float height = heightField.GetValue<float>();
                heightField.SetValue(height + Text.LineHeight);
                tr.Method("UpdateWindowSize").GetValue();

                Rect row = new Rect(inRect.x, inRect.y, inRect.width, Text.LineHeight);
                bool enabled = UI.RegionBorderOverlay.Enabled;
                Widgets.CheckboxLabeled(row, "Draw region borders", ref enabled);
                UI.RegionBorderOverlay.Enabled = enabled;
                inRect.y += Text.LineHeight;
            }
            catch
            {
                // If MMF's private layout members ever change, fail quiet rather than break the panel.
            }
        }
    }
}
