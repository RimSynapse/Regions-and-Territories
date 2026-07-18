using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimSynapse.RegionsAndTerritories
{
    public static class DwellingStructureGenerator
    {
        public static void Generate(Map map, int count)
        {
            if (map == null || count <= 0) return;

            // Cap individual dwellings at 5; if population is larger, allocate the rest to Barracks (max 3 barracks)
            int targetDwellings = Math.Min(count, 5);
            int remainingPop = count - targetDwellings;
            int targetBarracks = 0;
            if (remainingPop > 0)
            {
                targetBarracks = Math.Min((remainingPop + 3) / 4, 3);
            }

            List<IntVec3> spawnedBaseLocs = new List<IntVec3>();

            // Fetch defs safely
            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef doorDef = ThingDefOf.Door;
            ThingDef woodLog = ThingDefOf.WoodLog;
            ThingDef campfireDef = ThingDefOf.Campfire;
            ThingDef bedDef = ThingDefOf.Bed;
            ThingDef chairDef = DefDatabase<ThingDef>.GetNamed("DiningChair", false) ?? DefDatabase<ThingDef>.GetNamed("Stool", false) ?? ThingDefOf.Wall;
            
            ThingDef fenceDef = DefDatabase<ThingDef>.GetNamed("Fence", false) ?? ThingDefOf.Wall;
            ThingDef fenceGateDef = DefDatabase<ThingDef>.GetNamed("FenceGate", false) ?? ThingDefOf.Door;
            ThingDef penMarkerDef = DefDatabase<ThingDef>.GetNamed("PenMarker", false);

            TerrainDef woodFloor = DefDatabase<TerrainDef>.GetNamed("WoodPlankFloor", false) ?? TerrainDefOf.Concrete;
            TerrainDef soilTerrain = TerrainDefOf.Soil;

            // 1. Generate Individual Dwellings (5x5 exterior, 3x3 interior)
            for (int i = 0; i < targetDwellings; i++)
            {
                IntVec3 startSpot = FindValidSpot(map, spawnedBaseLocs);
                if (startSpot == IntVec3.Invalid) continue;

                IntVec3 baseLoc = startSpot - new IntVec3(8, 0, 8);
                spawnedBaseLocs.Add(baseLoc);

                // Wall footprint is 5x5
                for (int x = baseLoc.x; x <= baseLoc.x + 4; x++)
                {
                    for (int z = baseLoc.z; z <= baseLoc.z + 4; z++)
                    {
                        IntVec3 c = new IntVec3(x, 0, z);
                        if (!c.InBounds(map)) continue;

                        ClearBlockers(c, map);

                        bool isBorderX = (x == baseLoc.x || x == baseLoc.x + 4);
                        bool isBorderZ = (z == baseLoc.z || z == baseLoc.z + 4);

                        if (isBorderX || isBorderZ)
                        {
                            // Door in center of the top wall
                            if (x == baseLoc.x + 2 && z == baseLoc.z + 4)
                            {
                                SpawnThing(doorDef, woodLog, c, map);
                            }
                            else
                            {
                                SpawnThing(wallDef, woodLog, c, map);
                            }
                        }
                        else
                        {
                            map.terrainGrid.SetTerrain(c, woodFloor);
                        }

                        map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                    }
                }

                // Spawn wooden bed inside (facing north)
                IntVec3 bedLoc = baseLoc + new IntVec3(1, 0, 1);
                ClearBlockers(bedLoc, map);
                Thing bedThing = ThingMaker.MakeThing(bedDef, woodLog);
                SetQualityAndCondition(bedThing);
                GenSpawn.Spawn(bedThing, bedLoc, map, Rot4.North);
                Building_Bed bed = bedThing as Building_Bed;

                // Spawn a chair inside
                IntVec3 chairLoc = baseLoc + new IntVec3(3, 0, 1);
                ClearBlockers(chairLoc, map);
                Thing chairThing = ThingMaker.MakeThing(chairDef, woodLog);
                SetQualityAndCondition(chairThing);
                GenSpawn.Spawn(chairThing, chairLoc, map, Rot4.North);

                // Campfire outside (near the door)
                IntVec3 campfireLoc = new IntVec3(baseLoc.x + 2, 0, baseLoc.z + 6);
                ClearBlockers(campfireLoc, map);
                SpawnThing(campfireDef, null, campfireLoc, map);

                System.Random random = new System.Random(baseLoc.x ^ baseLoc.z);
                bool spawnPen = random.NextDouble() < 0.5;

                if (spawnPen)
                {
                    // An empty 8x8 penned area
                    for (int x = baseLoc.x; x <= baseLoc.x + 7; x++)
                    {
                        for (int z = baseLoc.z + 9; z <= baseLoc.z + 16; z++)
                        {
                            IntVec3 c = new IntVec3(x, 0, z);
                            if (!c.InBounds(map)) continue;

                            ClearBlockers(c, map);

                            bool isBorderX = (x == baseLoc.x || x == baseLoc.x + 7);
                            bool isBorderZ = (z == baseLoc.z + 9 || z == baseLoc.z + 16);

                            if (isBorderX || isBorderZ)
                            {
                                if (x == baseLoc.x + 4 && z == baseLoc.z + 9)
                                {
                                    SpawnThing(fenceGateDef, woodLog, c, map);
                                }
                                else
                                {
                                    SpawnThing(fenceDef, woodLog, c, map);
                                }
                            }
                        }
                    }

                    if (penMarkerDef != null)
                    {
                        IntVec3 markerLoc = new IntVec3(baseLoc.x + 3, 0, baseLoc.z + 12);
                        ClearBlockers(markerLoc, map);
                        SpawnThing(penMarkerDef, woodLog, markerLoc, map);
                    }
                }
                else
                {
                    // Crop field
                    Zone_Growing zone = new Zone_Growing(map.zoneManager);
                    map.zoneManager.RegisterZone(zone);

                    ThingDef[] cropsList = new ThingDef[]
                    {
                        ThingDefOf.Plant_Potato,
                        DefDatabase<ThingDef>.GetNamed("Plant_Corn", false),
                        DefDatabase<ThingDef>.GetNamed("Plant_Rice", false),
                        DefDatabase<ThingDef>.GetNamed("Plant_Cotton", false),
                        DefDatabase<ThingDef>.GetNamed("Plant_Healroot", false)
                    };
                    
                    List<ThingDef> validCrops = cropsList.Where(crop => crop != null).ToList();
                    ThingDef selectedCrop = validCrops.Count > 0 ? validCrops[random.Next(validCrops.Count)] : ThingDefOf.Plant_Potato;

                    for (int x = baseLoc.x + 6; x <= baseLoc.x + 13; x++)
                    {
                        for (int z = baseLoc.z; z <= baseLoc.z + 7; z++)
                        {
                            IntVec3 c = new IntVec3(x, 0, z);
                            if (!c.InBounds(map)) continue;

                            ClearBlockers(c, map);
                            map.terrainGrid.SetTerrain(c, soilTerrain);
                            zone.AddCell(c);

                            Plant plant = (Plant)ThingMaker.MakeThing(selectedCrop);
                            plant.Growth = Rand.Range(0.15f, 0.85f);
                            GenSpawn.Spawn(plant, c, map);
                        }
                    }
                    zone.SetPlantDefToGrow(selectedCrop);
                }

                // Spawn resident pawn
                if (Rand.Value < 0.66f)
                {
                    SpawnResident(map, baseLoc + new IntVec3(2, 0, 2), bed);
                }
            }

            // 2. Generate Barracks (5x9 exterior, 3x7 interior)
            for (int i = 0; i < targetBarracks; i++)
            {
                IntVec3 startSpot = FindValidSpot(map, spawnedBaseLocs);
                if (startSpot == IntVec3.Invalid) continue;

                IntVec3 baseLoc = startSpot - new IntVec3(8, 0, 8);
                spawnedBaseLocs.Add(baseLoc);

                // Footprint is 5x9 (width 9 on X, height 5 on Z)
                for (int x = baseLoc.x; x <= baseLoc.x + 8; x++)
                {
                    for (int z = baseLoc.z; z <= baseLoc.z + 4; z++)
                    {
                        IntVec3 c = new IntVec3(x, 0, z);
                        if (!c.InBounds(map)) continue;

                        ClearBlockers(c, map);

                        bool isBorderX = (x == baseLoc.x || x == baseLoc.x + 8);
                        bool isBorderZ = (z == baseLoc.z || z == baseLoc.z + 4);

                        if (isBorderX || isBorderZ)
                        {
                            // Door in the center of the 9-length wall (at bottom, z = baseLoc.z)
                            if (x == baseLoc.x + 4 && z == baseLoc.z)
                            {
                                SpawnThing(doorDef, woodLog, c, map);
                            }
                            else
                            {
                                SpawnThing(wallDef, woodLog, c, map);
                            }
                        }
                        else
                        {
                            map.terrainGrid.SetTerrain(c, woodFloor);
                        }

                        map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                    }
                }

                // Spawn 4 beds lengthwise towards the door (North rotation, headboard against far top wall)
                // Bed X spots: baseLoc.x + 1, baseLoc.x + 3, baseLoc.x + 5, baseLoc.x + 7
                List<Building_Bed> barracksBeds = new List<Building_Bed>();
                for (int bedIndex = 0; bedIndex < 4; bedIndex++)
                {
                    int bx = baseLoc.x + 1 + (bedIndex * 2);
                    IntVec3 bLoc = new IntVec3(bx, 0, baseLoc.z + 2);
                    ClearBlockers(bLoc, map);
                    ClearBlockers(bLoc + new IntVec3(0, 0, 1), map);

                    Thing bedThing = ThingMaker.MakeThing(bedDef, woodLog);
                    SetQualityAndCondition(bedThing);
                    GenSpawn.Spawn(bedThing, bLoc, map, Rot4.North);
                    
                    if (bedThing is Building_Bed bed)
                    {
                        barracksBeds.Add(bed);
                    }
                }

                // Campfire outside (near the door)
                IntVec3 campfireLoc = new IntVec3(baseLoc.x + 4, 0, baseLoc.z - 2);
                ClearBlockers(campfireLoc, map);
                SpawnThing(campfireDef, null, campfireLoc, map);

                // Spawn residents for barracks (each gets assigned to one of the 4 beds)
                for (int bIndex = 0; bIndex < barracksBeds.Count; bIndex++)
                {
                    if (Rand.Value < 0.75f)
                    {
                        IntVec3 pLoc = new IntVec3(baseLoc.x + 1 + (bIndex * 2), 0, baseLoc.z + 1);
                        SpawnResident(map, pLoc, barracksBeds[bIndex]);
                    }
                }
            }
        }

        private static void SetQualityAndCondition(Thing thing)
        {
            if (thing == null) return;

            // Maximum quality is normal
            var compQuality = thing.TryGetComp<CompQuality>();
            if (compQuality != null)
            {
                QualityCategory qc = (QualityCategory)Rand.RangeInclusive(0, (int)QualityCategory.Normal);
                compQuality.SetQuality(qc, ArtGenerationContext.Outsider);
            }

            // Good condition (100% hitpoints)
            if (thing.def.useHitPoints)
            {
                thing.HitPoints = thing.MaxHitPoints;
            }
        }

        private static void SpawnResident(Map map, IntVec3 spawnLoc, Building_Bed bed)
        {
            if (!spawnLoc.InBounds(map)) return;

            Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.OutlanderCivil);
            if (faction == null)
            {
                faction = Find.FactionManager.AllFactions.FirstOrDefault(f => !f.HostileTo(Faction.OfPlayer) && !f.def.isPlayer);
            }
            if (faction == null)
            {
                faction = Find.FactionManager.OfAncients;
            }

            PawnKindDef kind = PawnKindDefOf.Colonist;
            if (faction.def.defName.Contains("Tribal"))
            {
                kind = DefDatabase<PawnKindDef>.GetNamed("Tribal_Warrior", false) ?? PawnKindDefOf.Colonist;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                kind,
                faction: faction,
                context: PawnGenerationContext.NonPlayer
            );
            Pawn pawn = PawnGenerator.GeneratePawn(request);

            GenSpawn.Spawn(pawn, spawnLoc, map);

            if (bed != null)
            {
                pawn.ownership.ClaimBedIfNonMedical(bed);
            }

            LordMaker.MakeNewLord(
                faction,
                new LordJob_DefendPoint(spawnLoc),
                map,
                new List<Pawn> { pawn }
            );
        }

        private static IntVec3 FindValidSpot(Map map, List<IntVec3> spawnedBaseLocs)
        {
            for (int i = 0; i < 3000; i++)
            {
                IntVec3 cell = CellFinder.RandomCell(map);
                if (cell.Walkable(map) && 
                    !cell.Fogged(map) && 
                    cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy) && 
                    cell.x > 25 && cell.x < map.Size.x - 25 && cell.z > 25 && cell.z < map.Size.z - 25)
                {
                    IntVec3 baseLoc = cell - new IntVec3(8, 0, 8);
                    
                    bool tooClose = false;
                    foreach (var other in spawnedBaseLocs)
                    {
                        if (baseLoc.DistanceToSquared(other) < 25 * 25)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose) continue;

                    bool clear = true;
                    for (int x = -2; x <= 22; x++)
                    {
                        for (int z = -2; z <= 22; z++)
                        {
                            IntVec3 c = baseLoc + new IntVec3(x, 0, z);
                            if (!c.InBounds(map))
                            {
                                clear = false;
                                break;
                            }
                            var terrain = c.GetTerrain(map);
                            if (terrain.IsWater || c.GetEdifice(map) != null)
                            {
                                clear = false;
                                break;
                            }
                        }
                        if (!clear) break;
                    }
                    if (clear) return cell;
                }
            }

            return IntVec3.Invalid;
        }

        private static void ClearBlockers(IntVec3 cell, Map map)
        {
            var blockers = cell.GetThingList(map).ToList();
            foreach (var b in blockers)
            {
                if (b.def.destroyable && b.def.category != ThingCategory.Pawn)
                {
                    b.Destroy();
                }
            }
        }

        private static void SpawnThing(ThingDef def, ThingDef stuff, IntVec3 cell, Map map)
        {
            if (def == null) return;
            Thing thing = ThingMaker.MakeThing(def, stuff);
            GenSpawn.Spawn(thing, cell, map);
        }
    }
}
