using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace RimSynapse.RegionsAndTerritories.Patches
{
    [HarmonyPatch(typeof(FactionGenerator), "GenerateFactionsIntoWorldLayer")]
    public static class Patch_FactionGenerator_GenerateFactionsIntoWorld
    {
        [HarmonyPrefix]
        public static bool Prefix(PlanetLayer layer, List<FactionDef> factions)
        {
            if (layer == null || layer.Def == null || layer.Def.defName != "Surface")
            {
                Log.Message($"[RimSynapse-RegionsAndTerritories] Bypassing custom faction generator for non-surface layer '{layer?.Def?.defName ?? "null"}'. Falling back to vanilla.");
                return true;
            }

            Log.Message("[RimSynapse-RegionsAndTerritories] Custom Faction Generation and Placement solver starting...\n" + new System.Diagnostics.StackTrace().ToString());

            World world = Find.World ?? Current.CreatingWorld;
            if (world == null || world.info == null || world.grid == null)
            {
                Log.Warning("[RimSynapse-RegionsAndTerritories] World, World.info, or World.grid is null! Falling back to vanilla generator.");
                return true;
            }

            if (factions == null)
            {
                factions = new List<FactionDef>();
                foreach (var def in DefDatabase<FactionDef>.AllDefsListForReading)
                {
                    if (!def.isPlayer && !def.hidden && def.defName != "PColony")
                    {
                        factions.Add(def);
                    }
                }
            }

            FactionManager factionManager = world.factionManager;
            if (factionManager == null)
            {
                Log.Warning("[RimSynapse-RegionsAndTerritories] FactionManager is null! Falling back to vanilla generator.");
                return true;
            }

            WorldGrid worldGrid = world.grid;
            WorldObjectsHolder worldObjects = world.worldObjects;

            var regionManager = world.GetComponent<SynapseRegionManager>();
            if (regionManager == null)
            {
                Log.Warning("[RimSynapse-RegionsAndTerritories] SynapseRegionManager is null! Falling back to vanilla generator.");
                return true;
            }

            regionManager.GenerateProvinces();

            float coverage = world.info.planetCoverage;
            
            int landTilesCount = 0;
            int totalTiles = worldGrid.TilesCount;
            for (int i = 0; i < totalTiles; i++)
            {
                if (!worldGrid[i].WaterCovered)
                {
                    landTilesCount++;
                }
            }

            int targetFactionCount = Mathf.RoundToInt(coverage * 30f * (landTilesCount / 40000f));
            if (targetFactionCount < 5) targetFactionCount = 5;
            if (targetFactionCount > 35) targetFactionCount = 35;

            var canExistOnLayerMethod = typeof(FactionGenerator).GetMethod("CanExistOnLayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            List<FactionDef> poolToClone = DefDatabase<FactionDef>.AllDefs
                .Where(f => !f.isPlayer && !f.hidden && f.defName != "PColony")
                .Where(f => {
                    if (canExistOnLayerMethod != null)
                    {
                        return (bool)canExistOnLayerMethod.Invoke(null, new object[] { layer, f });
                    }
                    return true;
                })
                .ToList();

            List<FactionDef> finalDefs = new List<FactionDef>();
            foreach (var def in factions)
            {
                if (canExistOnLayerMethod == null || (bool)canExistOnLayerMethod.Invoke(null, new object[] { layer, def }))
                {
                    finalDefs.Add(def);
                }
            }

            if (poolToClone.Any())
            {
                while (finalDefs.Count(d => !d.isPlayer && !d.hidden) < targetFactionCount)
                {
                    finalDefs.Add(poolToClone.RandomElement());
                }
            }

            WorldObjectDef origSettlementDef = null;
            System.Reflection.FieldInfo settlementField = typeof(PlanetLayerDef).GetField("settlementWorldObjectDef", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (layer != null && layer.Def != null && settlementField != null)
            {
                origSettlementDef = (WorldObjectDef)settlementField.GetValue(layer.Def);
                settlementField.SetValue(layer.Def, null);
            }

            try
            {
                List<Faction> generatedFactions = new List<Faction>();
                foreach (var def in finalDefs)
                {
                    Faction faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def, default(IdeoGenerationParms), true));
                    if (faction != null)
                    {
                        factionManager.Add(faction);
                        generatedFactions.Add(faction);
                    }
                }

                foreach (FactionDef def in DefDatabase<FactionDef>.AllDefs)
                {
                    if (def.hidden && factionManager.FirstFactionOfDef(def) == null)
                    {
                        Faction faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def, default(IdeoGenerationParms), true));
                        if (faction != null)
                        {
                            factionManager.Add(faction);
                        }
                    }
                }
            }
            finally
            {
                if (layer != null && layer.Def != null && settlementField != null)
                {
                    settlementField.SetValue(layer.Def, origSettlementDef);
                }
            }

            foreach (var f1 in factionManager.AllFactions)
            {
                foreach (var f2 in factionManager.AllFactions)
                {
                    if (f1 != f2 && f1.RelationWith(f2, true) == null)
                    {
                        f1.RelationWith(f2, true);
                    }
                }
            }

            List<int> placedBases = new List<int>();
            var allNPCFactions = factionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.def.hidden)
                .Where(f => {
                    if (canExistOnLayerMethod != null)
                    {
                        return (bool)canExistOnLayerMethod.Invoke(null, new object[] { layer, f.def });
                    }
                    return true;
                })
                .ToList();

            var allProvinces = regionManager.Provinces;
            if (!allProvinces.Any())
            {
                Log.Warning("[RimSynapse-RegionsAndTerritories] No provinces generated! Falling back to vanilla generator.");
                return true;
            }
            Faction playerFaction = Find.FactionManager?.OfPlayer;

            // Global tracking of provinces that already contain a settlement
            HashSet<GeographicProvince> occupiedProvinces = new HashSet<GeographicProvince>();

            // Calculate raw target base counts for all NPC factions
            Dictionary<Faction, int> factionTargetBases = new Dictionary<Faction, int>();
            int totalHostileTarget = 0;
            int totalNonHostileTarget = 0;

            float mapSizeMult = coverage / 0.05f;
            float landRatio = (float)landTilesCount / totalTiles;
            float landRatioMult = landRatio / 0.05f;

            foreach (var faction in allNPCFactions)
            {
                float rngVal = GetFactionRng(faction);
                int baseCount = Mathf.RoundToInt((mapSizeMult * landRatioMult * rngVal) / 6f);
                baseCount = Mathf.Clamp(baseCount, 1, 40);

                factionTargetBases[faction] = baseCount;

                bool isHostile = (playerFaction != null) ? faction.HostileTo(playerFaction) : faction.def.permanentEnemy;
                if (isHostile)
                {
                    totalHostileTarget += baseCount;
                }
                else
                {
                    totalNonHostileTarget += baseCount;
                }
            }

            // Adjust for threat percentage cap (default 50%)
            float maxThreatPercent = FactionPlacementSettings.maxThreatPercent;
            if (maxThreatPercent < 1.0f && totalNonHostileTarget > 0)
            {
                int maxHostileAllowed = Mathf.RoundToInt(totalNonHostileTarget * maxThreatPercent / (1f - maxThreatPercent));
                if (totalHostileTarget > maxHostileAllowed)
                {
                    float hostileScale = (float)maxHostileAllowed / totalHostileTarget;
                    foreach (var faction in allNPCFactions)
                    {
                        bool isHostile = (playerFaction != null) ? faction.HostileTo(playerFaction) : faction.def.permanentEnemy;
                        if (isHostile)
                        {
                            int scaled = Mathf.RoundToInt(factionTargetBases[faction] * hostileScale);
                            factionTargetBases[faction] = Mathf.Max(1, scaled);
                        }
                    }
                }
            }

            // Adjust for total settlement count cap (default 50% of regions)
            int totalBasesAfterThreat = factionTargetBases.Values.Sum();
            float maxSettlementPercentOfRegions = FactionPlacementSettings.maxSettlementPercentOfRegions;
            int maxBasesAllowed = Mathf.RoundToInt(allProvinces.Count * maxSettlementPercentOfRegions);

            if (totalBasesAfterThreat > maxBasesAllowed)
            {
                float globalScale = (float)maxBasesAllowed / totalBasesAfterThreat;
                foreach (var faction in allNPCFactions)
                {
                    int scaled = Mathf.RoundToInt(factionTargetBases[faction] * globalScale);
                    factionTargetBases[faction] = Mathf.Max(1, scaled);
                }
            }

            // Sort and interleave NPC Factions: 1 Industrial, then 1 Tribal, etc.
            var industrials = allNPCFactions
                .Where(f => f.def.techLevel == TechLevel.Industrial)
                .OrderBy(f => GetCategoryPriority(f))
                .ThenBy(f => (playerFaction != null && f.HostileTo(playerFaction)) ? 1 : 0)
                .ToList();

            var tribals = allNPCFactions
                .Where(f => f.def.techLevel < TechLevel.Industrial)
                .OrderBy(f => GetCategoryPriority(f))
                .ThenBy(f => (playerFaction != null && f.HostileTo(playerFaction)) ? 1 : 0)
                .ToList();

            var others = allNPCFactions
                .Where(f => f.def.techLevel > TechLevel.Industrial)
                .OrderBy(f => GetCategoryPriority(f))
                .ThenBy(f => (playerFaction != null && f.HostileTo(playerFaction)) ? 1 : 0)
                .ToList();

            List<Faction> alternatingFactions = new List<Faction>();
            int indIndex = 0;
            int triIndex = 0;
            int othIndex = 0;

            while (indIndex < industrials.Count || triIndex < tribals.Count || othIndex < others.Count)
            {
                if (indIndex < industrials.Count)
                {
                    alternatingFactions.Add(industrials[indIndex++]);
                }
                if (triIndex < tribals.Count)
                {
                    alternatingFactions.Add(tribals[triIndex++]);
                }
                if (othIndex < others.Count)
                {
                    alternatingFactions.Add(others[othIndex++]);
                }
            }

            foreach (var faction in alternatingFactions)
            {
                var profile = FactionPlacementSettings.GetProfile(faction.def);
                if (profile == null) continue;

                int baseCount = factionTargetBases.ContainsKey(faction) ? factionTargetBases[faction] : 5;

                Dictionary<int, float> tileScores = new Dictionary<int, float>();
                for (int t = 0; t < totalTiles; t++)
                {
                    Tile tileData = worldGrid[t];
                    if (tileData.WaterCovered || tileData.hilliness == Hilliness.Impassable || (tileData.PrimaryBiome != null && (tileData.PrimaryBiome.impassable || tileData.PrimaryBiome.defName == "SeaIce")))
                    {
                        tileScores[t] = -9999f;
                        continue;
                    }

                    if (!faction.def.allowedArrivalTemperatureRange.Includes(tileData.temperature))
                    {
                        tileScores[t] = -9999f;
                        continue;
                    }

                    float mineralVal = 0.5f;
                    if (tileData.hilliness == Hilliness.SmallHills) mineralVal = 1.0f;
                    else if (tileData.hilliness == Hilliness.LargeHills) mineralVal = 2.0f;
                    else if (tileData.hilliness == Hilliness.Mountainous) mineralVal = 3.0f;

                    float nutritionVal = tileData.PrimaryBiome != null ? tileData.PrimaryBiome.plantDensity : 0.5f;
                    float forageVal = tileData.PrimaryBiome != null ? tileData.PrimaryBiome.forageability : 0.5f;
                    float biomassVal = tileData.PrimaryBiome != null ? tileData.PrimaryBiome.TreeDensity : 0.5f;
                    float grazingVal = (tileData.hilliness == Hilliness.Flat) ? nutritionVal * 2f : nutritionVal;
                    float hospVal = nutritionVal * 2f + forageVal;

                    float score = 0f;
                    score += profile.mineralWeight * mineralVal;
                    score += profile.nutritionWeight * nutritionVal;
                    score += profile.forageWeight * forageVal;
                    score += profile.grazingWeight * grazingVal;
                    score += profile.huntingWeight * biomassVal;

                    if (profile.marginWeight > 0f)
                    {
                        score += profile.marginWeight * Mathf.Max(0f, 3.0f - hospVal);
                    }

                    tileScores[t] = score;
                }

                Dictionary<GeographicProvince, float> provinceScores = new Dictionary<GeographicProvince, float>();
                foreach (var p in allProvinces)
                {
                    // Do not place settlements in area of less than 20 tiles
                    if (p.tiles == null)
                    {
                        provinceScores[p] = -9999f;
                        continue;
                    }
                    if (p.tiles.Count < 20)
                    {
                        provinceScores[p] = -9999f;
                        continue;
                    }

                    var validTiles = p.tiles.Where(t => tileScores.ContainsKey(t) && tileScores[t] > -9999f).ToList();
                    if (validTiles.Count == 0)
                    {
                        provinceScores[p] = -9999f;
                        continue;
                    }
                    provinceScores[p] = validTiles.Average(t => tileScores[t]);
                }

                List<GeographicProvince> factionProvinces = new List<GeographicProvince>();
                List<int> factionBases = new List<int>();

                // Define hard adjacency requirement: friendly and neutral factions (excluding the Empire)
                bool isFriendlyOrNeutral = (playerFaction != null) ? !faction.HostileTo(playerFaction) : !faction.def.permanentEnemy;
                bool hasHardAdjacency = isFriendlyOrNeutral && (faction.def.defName != "Empire");

                for (int b = 0; b < baseCount; b++)
                {
                    GeographicProvince chosenProvince = null;
                    string factionId = faction.GetUniqueLoadID();

                    // Pre-calculate properties for all provinces (excluding already occupied ones)
                    var allCandidates = allProvinces
                        .Where(p => !occupiedProvinces.Contains(p))
                        .Select(p => {
                            float suitability = provinceScores.ContainsKey(p) ? provinceScores[p] : -9999f;
                            if (suitability > -9999f && faction.def.techLevel < TechLevel.Industrial)
                            {
                                suitability += GetTribalBetweennessBonus(p, placedBases, worldGrid);
                            }

                            if (suitability <= -9999f) return new { Province = p, Score = -9999f, ThisFactionCount = 999, GlobalCount = 999, IsAdjacent = false, Dist = 9999f, SharedBorders = 0, BarrierCount = 0 };

                            int thisFactionCount = p.owningFactionIds.Count(id => id == factionId);
                            int globalCount = p.owningFactionIds.Count;
                            bool isAdjacent = factionProvinces.Any() && IsProvinceAdjacentToAny(p, factionProvinces, regionManager, worldGrid);

                            float minAllyDist = 0f;
                            if (factionProvinces.Any())
                            {
                                minAllyDist = 9999f;
                                foreach (var ownP in factionProvinces)
                                {
                                    float dist = GetProvinceDistance(p, ownP, worldGrid);
                                    if (dist < minAllyDist) minAllyDist = dist;
                                }
                            }

                            int sharedBorders = factionProvinces.Any() ? GetSharedBorderCount(p, factionProvinces, regionManager, worldGrid) : 0;
                            int barrierCount = GetBarrierBorderCount(p, worldGrid);

                            return new { Province = p, Score = suitability, ThisFactionCount = thisFactionCount, GlobalCount = globalCount, IsAdjacent = isAdjacent, Dist = minAllyDist, SharedBorders = sharedBorders, BarrierCount = barrierCount };
                        })
                        .Where(x => x.Score > -9999f);

                    // For factions with hard adjacency requirement, subsequent bases MUST be adjacent (if any adjacent valid provinces exist)
                    if (b > 0 && hasHardAdjacency && factionProvinces.Any())
                    {
                        var adjacentOnly = allCandidates
                            .Where(x => x.IsAdjacent)
                            .Where(x => x.ThisFactionCount == 0)
                            .ToList();
                        if (adjacentOnly.Any())
                        {
                            allCandidates = adjacentOnly;
                        }
                        else
                        {
                            // If there are NO adjacent tiles/provinces, place it to the nearest not claimed region without a settlement
                            var nearestUnclaimedUnoccupied = allCandidates
                                .Where(x => x.GlobalCount == 0)
                                .OrderBy(x => x.Dist)
                                .ToList();

                            if (nearestUnclaimedUnoccupied.Any())
                            {
                                allCandidates = nearestUnclaimedUnoccupied;
                            }
                        }
                    }

                    // Weighting compact perimeter vs resource optimization based on size threshold (5 provinces)
                    bool prioritizePerimeter = (factionProvinces.Count > 5);

                    var sortedCandidates = allCandidates
                        .OrderBy(x => x.ThisFactionCount)
                        .ThenBy(x => x.GlobalCount);

                    if (prioritizePerimeter)
                    {
                        // Prioritize compact borders (high shared borders) and natural barriers/mountains first to keep territory from snaking
                        sortedCandidates = sortedCandidates
                            .ThenByDescending(x => x.SharedBorders)
                            .ThenByDescending(x => x.BarrierCount)
                            .ThenBy(x => x.Dist)
                            .ThenByDescending(x => x.Score);
                    }
                    else
                    {
                        // Prioritize resources/suitability first, while still favoring barrier anchoring
                        sortedCandidates = sortedCandidates
                            .ThenByDescending(x => x.Score)
                            .ThenByDescending(x => x.BarrierCount)
                            .ThenByDescending(x => x.IsAdjacent ? 1 : 0)
                            .ThenBy(x => x.Dist)
                            .ThenByDescending(x => x.SharedBorders);
                    }

                    var candidatesList = sortedCandidates.ToList();

                    if (candidatesList.Any())
                    {
                        chosenProvince = candidatesList[0].Province;
                    }

                    if (chosenProvince != null)
                    {
                        int chosenTile = FindBestTileInProvince(chosenProvince, factionBases, placedBases, tileScores, worldGrid);

                        if (chosenTile != -1)
                        {
                            Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                            settlement.Tile = chosenTile;
                            settlement.SetFaction(faction);
                            settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
                            worldObjects.Add(settlement);

                            // Mark the placement order for this settlement
                            regionManager.SetSettlementPlacementOrder(chosenTile, b + 1);

                            factionBases.Add(chosenTile);
                            placedBases.Add(chosenTile);

                            if (!factionProvinces.Contains(chosenProvince))
                            {
                                factionProvinces.Add(chosenProvince);
                                if (!chosenProvince.owningFactionIds.Contains(factionId))
                                {
                                    chosenProvince.owningFactionIds.Add(factionId);
                                }
                            }
                            occupiedProvinces.Add(chosenProvince);
                        }
                    }
                }

                Log.Message($"[RimSynapse-RegionsAndTerritories] Placed {factionBases.Count} bases across {factionProvinces.Count} provinces for faction: {faction.Name}");
            }

            // Redistribute NPC faction colors deterministically to ensure high vibrance and distinct visual separation
            var assignableFactions = factionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.def.hidden && f.def.defName != "Empire")
                .ToList();

            if (assignableFactions.Any())
            {
                System.Random colorRand = new System.Random(Find.World.info.Seed);
                var shuffled = assignableFactions.OrderBy(x => colorRand.Next()).ToList();
                for (int i = 0; i < shuffled.Count; i++)
                {
                    float hue = (float)i / shuffled.Count;
                    Color uniqueColor = Color.HSVToRGB(hue, 0.60f, 0.90f);
                    shuffled[i].color = uniqueColor;
                }
            }

            RoadGeneratorHelper.GenerateRoadsBetweenBases();

            // Refresh the population density cache since new settlements have been placed
            PopulationDensityUtility.MarkCacheDirty();

            Log.Message("[RimSynapse-RegionsAndTerritories] Custom Faction Generation and Placement completed successfully.");
            return false;
        }

        private static int FindBestTileInProvince(GeographicProvince province, List<int> sameFactionBases, List<int> allPlacedBases, Dictionary<int, float> tileScores, WorldGrid worldGrid)
        {
            var candidateTiles = province.tiles
                .Where(t => tileScores.ContainsKey(t) && tileScores[t] > -9999f && !allPlacedBases.Contains(t))
                .ToList();

            if (!candidateTiles.Any()) return -1;
            if (candidateTiles.Count == 1) return candidateTiles[0];

            // Compute province centroid
            Vector3 centroid = Vector3.zero;
            foreach (int t in province.tiles)
            {
                centroid += worldGrid.GetTileCenter(t);
            }
            centroid /= province.tiles.Count;

            HashSet<int> provinceTiles = new HashSet<int>(province.tiles);

            // Compute scores
            var tileDataList = new List<TileScoreData>();
            float minRes = float.MaxValue, maxRes = float.MinValue;
            float minCentroidDist = float.MaxValue, maxCentroidDist = float.MinValue;
            float minPop = float.MaxValue, maxPop = float.MinValue;

            foreach (int t in candidateTiles)
            {
                float res = tileScores[t];
                if (res < minRes) minRes = res;
                if (res > maxRes) maxRes = res;

                float dist = (worldGrid.GetTileCenter(t) - centroid).magnitude;
                if (dist < minCentroidDist) minCentroidDist = dist;
                if (dist > maxCentroidDist) maxCentroidDist = dist;

                float pop = FactionPlacementUtility.EvaluatePopulationRetention(t, provinceTiles);
                if (pop < minPop) minPop = pop;
                if (pop > maxPop) maxPop = pop;

                tileDataList.Add(new TileScoreData { Tile = t, ResScore = res, CentroidDist = dist, PopRetention = pop });
            }

            // Calculate final score: 20% centrality, 40% resources, 40% population retention
            var sortedCandidates = tileDataList.Select(data =>
            {
                float normRes = (maxRes > minRes) ? (data.ResScore - minRes) / (maxRes - minRes) : 1.0f;
                float normCentroidDist = (maxCentroidDist > minCentroidDist) ? (data.CentroidDist - minCentroidDist) / (maxCentroidDist - minCentroidDist) : 0.0f;
                float centrality = 1.0f - normCentroidDist;
                float normPop = (maxPop > minPop) ? (data.PopRetention - minPop) / (maxPop - minPop) : 1.0f;

                float finalScore = 0.4f * normRes + 0.2f * centrality + 0.4f * normPop;
                return new { Tile = data.Tile, FinalScore = finalScore };
            })
            .OrderByDescending(x => x.FinalScore)
            .Select(x => x.Tile)
            .ToList();

            foreach (var tile in sortedCandidates)
            {
                bool tooCloseToRival = false;
                foreach (var otherBase in allPlacedBases)
                {
                    if (sameFactionBases.Contains(otherBase)) continue;
                    float dist = worldGrid.ApproxDistanceInTiles(tile, otherBase);
                    if (dist < 8f)
                    {
                        tooCloseToRival = true;
                        break;
                    }
                }
                if (!tooCloseToRival) return tile;
            }

            foreach (var tile in sortedCandidates)
            {
                bool tooCloseToRival = false;
                foreach (var otherBase in allPlacedBases)
                {
                    if (sameFactionBases.Contains(otherBase)) continue;
                    float dist = worldGrid.ApproxDistanceInTiles(tile, otherBase);
                    if (dist < 4f)
                    {
                        tooCloseToRival = true;
                        break;
                    }
                }
                if (!tooCloseToRival) return tile;
            }

            return sortedCandidates[0];
        }

        private struct TileScoreData
        {
            public int Tile;
            public float ResScore;
            public float CentroidDist;
            public float PopRetention;
        }

        private static bool IsProvinceAdjacentToAny(GeographicProvince p, List<GeographicProvince> existing, SynapseRegionManager manager, WorldGrid worldGrid)
        {
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            foreach (int tile in p.tiles)
            {
                neighbors.Clear();
                worldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    int neighborProvinceId = manager.GetProvinceId(n.tileId);
                    if (neighborProvinceId != -1 && existing.Any(ep => ep.id == neighborProvinceId))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static float GetProvinceDistance(GeographicProvince p1, GeographicProvince p2, WorldGrid worldGrid)
        {
            if (p1.tiles.Count == 0 || p2.tiles.Count == 0) return 9999f;
            return worldGrid.ApproxDistanceInTiles(p1.tiles[0], p2.tiles[0]);
        }

        private static int GetCategoryPriority(Faction faction)
        {
            var profile = FactionPlacementSettings.GetProfile(faction.def);
            if (profile != null)
            {
                return profile.placementOrder;
            }
            if (faction.def.defName == "Empire") return 2;
            if (faction.def.techLevel == TechLevel.Industrial) return 1;
            if (faction.def.techLevel >= TechLevel.Spacer) return 3;
            return 4; // Tribal
        }

        private static int GetSharedBorderCount(GeographicProvince p, List<GeographicProvince> existing, SynapseRegionManager manager, WorldGrid worldGrid)
        {
            HashSet<int> sharedAdjacentProvinces = new HashSet<int>();
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            foreach (int tile in p.tiles)
            {
                neighbors.Clear();
                worldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    int neighborProvinceId = manager.GetProvinceId(n.tileId);
                    if (neighborProvinceId != -1 && neighborProvinceId != p.id)
                    {
                        var matchingProv = existing.FirstOrDefault(ep => ep.id == neighborProvinceId);
                        if (matchingProv != null)
                        {
                            sharedAdjacentProvinces.Add(matchingProv.id);
                        }
                    }
                }
            }
            return sharedAdjacentProvinces.Count;
        }

        private static int GetBarrierBorderCount(GeographicProvince p, WorldGrid worldGrid)
        {
            int barrierCount = 0;
            List<RimWorld.Planet.PlanetTile> neighbors = new List<RimWorld.Planet.PlanetTile>();
            foreach (int tile in p.tiles)
            {
                neighbors.Clear();
                worldGrid.GetTileNeighbors(tile, neighbors);
                foreach (var n in neighbors)
                {
                    Tile nTile = worldGrid[n.tileId];
                    if (nTile.hilliness == Hilliness.Impassable || nTile.WaterCovered || (nTile.PrimaryBiome != null && nTile.PrimaryBiome.impassable))
                    {
                        barrierCount++;
                    }
                }
            }
            return barrierCount;
        }

        private static float GetFactionRng(Faction faction)
        {
            if (faction.def.defName.ToLower().Contains("pirate") || faction.def.label.ToLower().Contains("pirate"))
            {
                return UnityEngine.Random.Range(1f, 3f);
            }
            if (faction.def.techLevel == TechLevel.Industrial)
            {
                return UnityEngine.Random.Range(1f, 5f);
            }
            if (faction.def.techLevel >= TechLevel.Spacer)
            {
                return UnityEngine.Random.Range(1f, 2f);
            }
            return UnityEngine.Random.Range(1f, 2f); // Tribal / default
        }

        private static float GetTribalBetweennessBonus(GeographicProvince p, List<int> allPlacedBases, WorldGrid worldGrid)
        {
            if (p.tiles.Count == 0 || !allPlacedBases.Any()) return 0f;

            // Find all placed bases that belong to Industrial factions
            Dictionary<string, List<int>> industrialBasesByFaction = new Dictionary<string, List<int>>();
            foreach (int tile in allPlacedBases)
            {
                var settlement = Find.WorldObjects.Settlements.FirstOrDefault(s => s.Tile == tile);
                if (settlement != null && settlement.Faction != null && settlement.Faction.def.techLevel == TechLevel.Industrial)
                {
                    string fId = settlement.Faction.GetUniqueLoadID();
                    if (!industrialBasesByFaction.ContainsKey(fId))
                    {
                        industrialBasesByFaction[fId] = new List<int>();
                    }
                    industrialBasesByFaction[fId].Add(tile);
                }
            }

            if (industrialBasesByFaction.Count < 2) return 0f;

            // Calculate min distance to each industrial faction
            List<float> minDists = new List<float>();
            int tileCenter = p.tiles[0];

            foreach (var kvp in industrialBasesByFaction)
            {
                float minDist = 9999f;
                foreach (int baseTile in kvp.Value)
                {
                    float dist = worldGrid.ApproxDistanceInTiles(tileCenter, baseTile);
                    if (dist < minDist) minDist = dist;
                }
                minDists.Add(minDist);
            }

            minDists.Sort();

            float minDistF1 = minDists[0];
            float minDistF2 = minDists[1];

            // If both are within 30 tiles, calculate betweenness
            if (minDistF1 < 30f && minDistF2 < 30f)
            {
                return 50f / (minDistF1 + minDistF2);
            }

            return 0f;
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), "GenerateWorld")]
    public static class Patch_WorldGenerator_GenerateWorld
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            Log.Message("[RimSynapse-RegionsAndTerritories] WorldGenerator.GenerateWorld PREFIX is executing!");
        }
    }

    [HarmonyPatch(typeof(WorldGenStep_Factions), "GenerateFresh")]
    public static class Patch_WorldGenStep_Factions_GenerateFresh
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            Log.Message("[RimSynapse-RegionsAndTerritories] WorldGenStep_Factions.GenerateFresh PREFIX is executing!\n" + new System.Diagnostics.StackTrace().ToString());
        }
    }

    [HarmonyPatch(typeof(WorldObjectsHolder), "Add")]
    public static class Patch_WorldObjectsHolder_Add
    {
        [HarmonyPostfix]
        public static void Postfix(WorldObject o)
        {
            if (o is Settlement || o.GetType().Name == "WorldSettlementFC")
            {
                if (o.Faction != null)
                {
                    World world = Find.World;
                    if (world != null)
                    {
                        var regionManager = world.GetComponent<SynapseRegionManager>();
                        if (regionManager != null)
                        {
                            // Only set if not already set (to preserve initial generation indices)
                            if (regionManager.GetSettlementPlacementOrder(o.Tile) == -1)
                            {
                                int nextOrder = regionManager.GetNextPlacementOrderForFaction(o.Faction);
                                regionManager.SetSettlementPlacementOrder(o.Tile, nextOrder);
                            }
                        }
                    }
                }
            }
        }
    }
}
