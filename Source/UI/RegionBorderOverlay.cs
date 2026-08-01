using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimSynapse.RegionsAndTerritories.UI
{
    /// <summary>
    /// State for the global region-border overlay (#53): a per-frame world layer draws the division
    /// lines between provinces regardless of the active map mode, so ownership can be read at a glance
    /// without turning on territory shading. Toggled from Map Mode Framework's Draw Settings panel (see
    /// Patch_MapModeUI_DrawSettings). The geometry lives in <see cref="WorldLayer_RegionBorders"/>,
    /// which builds one coloured submesh per owning faction; this class only holds the toggle, a build
    /// version used to force a rebuild, and a small material cache keyed by colour.
    /// </summary>
    public static class RegionBorderOverlay
    {
        public static bool Enabled = true;   // default on so it is visible immediately; the checkbox toggles it

        private static int version;
        public static int Version => version;

        /// <summary>Force the border layer to rebuild its mesh on the next frame (region layout changed).</summary>
        public static void Invalidate() => version++;

        private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

        public static Material MaterialFor(Color color)
        {
            if (!materials.TryGetValue(color, out Material mat) || mat == null)
            {
                mat = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, color, 3600);
                materials[color] = mat;
            }
            return mat;
        }
    }
}
