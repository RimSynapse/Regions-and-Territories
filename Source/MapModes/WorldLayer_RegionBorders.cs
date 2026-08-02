using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MapModeFramework;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimSynapse.RegionsAndTerritories
{
    /// <summary>
    /// Auto-discovered world draw layer that renders the global region-border overlay (#53): the outline
    /// of every land province, coloured by its owning faction (white when unclaimed), drawn on top of
    /// whatever map mode is active. RimWorld instantiates every <see cref="WorldDrawLayer"/> subclass
    /// and renders it each frame, so no registration is needed. Geometry is built as submeshes (one per
    /// colour) the same way Map Mode Framework outlines its own regions — the proven path — and the base
    /// Render draws them; the overlay is hidden simply by skipping that Render when the toggle is off.
    /// </summary>
    public class WorldLayer_RegionBorders : WorldDrawLayer
    {
        private int builtVersion = -1;
        private bool built;
        private static bool loggedCtor;
        private static bool loggedRegen;
        private const float Lift = 0.015f;   // raise above the surface so lines beat z-fighting with terrain
        private const float Width = 0.6f;   // line thickness (inset distance toward the tile centre)

        private static readonly Color Unclaimed = new Color(1f, 1f, 1f, 0.85f);

        public WorldLayer_RegionBorders()
        {
            if (!loggedCtor)
            {
                loggedCtor = true;
                Log.Message("[RimSynapse-RegionsAndTerritories] WorldLayer_RegionBorders constructed (auto-discovered).");
            }
        }

        // Retry every frame until the mesh is actually built (provinces may not exist the first time the
        // world layers regenerate), then only rebuild when the layout version changes.
        public override bool ShouldRegenerate => !built || builtVersion != UI.RegionBorderOverlay.Version;

        public override IEnumerable Regenerate()
        {
            foreach (object item in base.Regenerate())
            {
                yield return item;
            }
            if (!loggedRegen)
            {
                loggedRegen = true;
                Log.Message("[RimSynapse-RegionsAndTerritories] WorldLayer_RegionBorders.Regenerate invoked.");
            }
            bool ok = false;
            try
            {
                ok = BuildBorders();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimSynapse-RegionsAndTerritories] Region border overlay build failed: {ex}");
                ok = true;   // don't spin forever on a hard error
            }
            if (ok)
            {
                built = true;
                builtVersion = UI.RegionBorderOverlay.Version;
            }
            FinalizeMesh(MeshParts.All);
        }

        // Registered as a GLOBAL draw layer, but this class extends the surface WorldDrawLayer whose
        // Position/Rotation getters dereference per-planet-layer state a global layer never gets (it
        // NREs in WorldDrawLayer.get_Position). The border mesh is already built in absolute world
        // space, so pin the layer transform to origin/identity to bypass that surface state.
        public override Vector3 Position => Vector3.zero;
        protected override Quaternion Rotation => Quaternion.identity;

        private static bool loggedRenderError;

        public override void Render()
        {
            if (!UI.RegionBorderOverlay.Enabled)
            {
                return;
            }
            try
            {
                base.Render();
            }
            catch (Exception ex)
            {
                if (!loggedRenderError)
                {
                    loggedRenderError = true;
                    Log.Warning($"[RimSynapse-RegionsAndTerritories] Region border overlay render error: {ex.Message}");
                }
            }
        }

        private bool BuildBorders()
        {
            if (Find.World == null)
            {
                return false;
            }
            var mgr = Find.World.GetComponent<SynapseRegionManager>();
            if (mgr == null)
            {
                return false;
            }
            var provinces = mgr.Provinces;   // triggers generation if needed
            if (provinces == null || provinces.Count == 0)
            {
                return false;   // not ready yet — ShouldRegenerate keeps retrying
            }

            WorldGrid grid = Find.WorldGrid;
            FactionManager factionManager = Find.FactionManager;
            var neighbors = new List<PlanetTile>();
            int edges = 0;

            for (int p = 0; p < provinces.Count; p++)
            {
                GeographicProvince province = provinces[p];
                if (province.provinceType != ProvinceType.Land || province.tiles == null || province.tiles.Count == 0)
                {
                    continue;
                }

                int pid = province.id;
                // One coloured submesh per owner; every region paints its OWN border (no dedupe), so a
                // shared seam shows both owners' lines — each drawn on the inside of its own hexes.
                LayerSubMesh subMesh = GetSubMesh(UI.RegionBorderOverlay.MaterialFor(OwnerColor(province, factionManager)));
                List<int> tiles = province.tiles;
                for (int i = 0; i < tiles.Count; i++)
                {
                    int t = tiles[i];
                    Vector3 center = grid.GetTileCenter(t);
                    neighbors.Clear();
                    grid.GetTileNeighbors(t, neighbors);
                    for (int k = 0; k < neighbors.Count; k++)
                    {
                        if (mgr.GetProvinceId(neighbors[k].tileId) == pid)
                        {
                            continue;   // interior edge, not a border
                        }
                        List<Vector3> shared = TileUtilities.GetSharedVertices(t, neighbors[k].tileId);
                        if (shared.Count < 2)
                        {
                            continue;
                        }
                        AddEdge(subMesh, shared[0], shared[1], center);
                        edges++;
                    }
                }
            }

            Log.Message($"[RimSynapse-RegionsAndTerritories] Region border overlay built: {edges} edge lines across {provinces.Count} provinces.");
            return true;
        }

        private static Color OwnerColor(GeographicProvince province, FactionManager factionManager)
        {
            if (province.owningFactionIds != null && province.owningFactionIds.Count > 0)
            {
                foreach (var id in province.owningFactionIds)
                {
                    Faction faction = factionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == id);
                    if (faction != null)
                    {
                        Color c = faction.Color;
                        return new Color(c.r, c.g, c.b, 0.9f);
                    }
                }
            }
            return Unclaimed;
        }

        private void AddEdge(LayerSubMesh subMesh, Vector3 a, Vector3 b, Vector3 tileCenter)
        {
            // Inset the two edge VERTICES toward the tile centre (not a per-edge perpendicular): two
            // boundary edges that meet at a hex corner then share the same inset point, so the strip
            // stays continuous around the tile instead of breaking into dashes. This also keeps the
            // line inside the region's hex, which is the point of the double.
            float inset = 0.05f * Width;
            Vector3 aIn = a + (tileCenter - a).normalized * inset;
            Vector3 bIn = b + (tileCenter - b).normalized * inset;

            a += a.normalized * Lift;
            b += b.normalized * Lift;
            aIn += aIn.normalized * Lift;
            bIn += bIn.normalized * Lift;

            int n = subMesh.verts.Count;
            subMesh.verts.Add(a);
            subMesh.verts.Add(b);
            subMesh.verts.Add(aIn);
            subMesh.verts.Add(bIn);
            // Both windings, so the strip renders regardless of which way the face points (no culling
            // gaps).
            subMesh.tris.Add(n);
            subMesh.tris.Add(n + 1);
            subMesh.tris.Add(n + 2);
            subMesh.tris.Add(n + 2);
            subMesh.tris.Add(n + 1);
            subMesh.tris.Add(n + 3);
            subMesh.tris.Add(n);
            subMesh.tris.Add(n + 2);
            subMesh.tris.Add(n + 1);
            subMesh.tris.Add(n + 1);
            subMesh.tris.Add(n + 2);
            subMesh.tris.Add(n + 3);
        }
    }
}
