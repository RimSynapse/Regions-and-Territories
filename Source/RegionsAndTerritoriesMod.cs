using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimSynapse.RegionsAndTerritories
{
    public class RegionsAndTerritoriesMod : Mod
    {
        public static FactionPlacementSettings Settings;

        public RegionsAndTerritoriesMod(ModContentPack content) : base(content)
        {
            Log.Message("[RimSynapse-RegionsAndTerritories] Initializing Regions and Territories Mod...");
            Settings = GetSettings<FactionPlacementSettings>();

            var harmony = new Harmony("rimsynapse.regionsandterritories");
            harmony.PatchAll();

            foreach (var m in harmony.GetPatchedMethods())
            {
                Log.Message($"[RimSynapse-RegionsAndTerritories] Successfully patched method: {m.DeclaringType.FullName}.{m.Name}");
            }

            TryRegisterPopulationDelegate();
        }

        private void TryRegisterPopulationDelegate()
        {
            try
            {
                var coreWorldCompType = GenTypes.GetTypeInAnyAssembly("RimSynapse.SynapseCoreWorldComponent");
                if (coreWorldCompType != null)
                {
                    var field = coreWorldCompType.GetField("GetPopulationDensityDelegate", BindingFlags.Public | BindingFlags.Static);
                    if (field != null)
                    {
                        Func<int, int> del = PopulationDensityUtility.GetPopulationAtTile;
                        field.SetValue(null, del);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Registered population delegate to RimSynapse Core successfully.");
                    }
                    else
                    {
                        Log.Warning("[RimSynapse-RegionsAndTerritories] Could not find GetPopulationDensityDelegate field in SynapseCoreWorldComponent.");
                    }
                }
                else
                {
                    Log.Message("[RimSynapse-RegionsAndTerritories] RimSynapse Core not detected. Skipping population delegate registration.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimSynapse-RegionsAndTerritories] Error registering population delegate: {ex.Message}");
            }
        }
    }
}
