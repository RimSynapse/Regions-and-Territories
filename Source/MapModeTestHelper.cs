using Verse;
using RimWorld;
using System.Linq;
using MapModeFramework;

namespace RimSynapse.RegionsAndTerritories
{
    public class MapModeTestHelper : GameComponent
    {
        private bool triggered = false;

        public MapModeTestHelper(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            if (triggered) return;
            if (Find.CurrentMap == null) return;

            // Wait until 300 ticks to ensure map is fully loaded
            if (Find.TickManager.TicksGame > 300)
            {
                triggered = true;
                Log.Warning("[MapModeTestHelper] Auto-opening World Map and enabling Faction Territories overlay...");
                
                // Open World Map
                Find.World.renderer.wantedMode = RimWorld.Planet.WorldRenderMode.Planet;

                // Find MapModeComponent and set current map mode
                var mapModeComp = MapModeComponent.Instance;
                if (mapModeComp != null)
                {
                    var targetMode = mapModeComp.mapModes.FirstOrDefault(m => m.def.defName == "SynapseFactionTerritory");
                    if (targetMode != null)
                    {
                        mapModeComp.SwitchMapMode(targetMode);
                        Log.Warning("[MapModeTestHelper] Successfully activated Faction Territories map mode!");
                    }
                    else
                    {
                        Log.Error("[MapModeTestHelper] Could not find map mode def 'SynapseFactionTerritory'!");
                    }
                }
                else
                {
                    Log.Error("[MapModeTestHelper] MapModeComponent.Instance is null!");
                }

                // Run Empires integration tests
                var fcAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Empire");
                if (fcAssembly != null)
                {
                    Log.Warning("[MapModeTestHelper] Running FactionColonies unit/integration tests...");
                    var runnerType = fcAssembly.GetType("FactionColonies.EmpireTestRunner");
                    if (runnerType != null)
                    {
                        var runTestsMethod = runnerType.GetMethod("RunTests", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (runTestsMethod != null)
                        {
                            Log.Warning("[MapModeTestHelper] Running standard tests...");
                            try { runTestsMethod.Invoke(null, new object[] { null, false }); } catch (System.Exception ex) { Log.Error("[MapModeTestHelper] Standard tests failed: " + ex); }
                            
                            Log.Warning("[MapModeTestHelper] Running destructive tests...");
                            try { runTestsMethod.Invoke(null, new object[] { null, true }); } catch (System.Exception ex) { Log.Error("[MapModeTestHelper] Destructive tests failed: " + ex); }
                        }
                    }
                }
            }
        }
    }
}
