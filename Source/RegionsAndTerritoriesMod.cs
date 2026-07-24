using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using RimWorld;
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
            TryPatchEmpires(harmony);
            TryPatchVOE(harmony);
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

        private void TryPatchEmpires(Harmony harmony)
        {
            try
            {
                var resourceFcType = GenTypes.GetTypeInAnyAssembly("FactionColonies.ResourceFC");
                var settlementMilitaryType = GenTypes.GetTypeInAnyAssembly("FactionColonies.WorldObjectComp_SettlementMilitary");

                if (resourceFcType != null)
                {
                    var originalBase = AccessTools.Method(resourceFcType, "CalculateProductionBase");
                    if (originalBase != null)
                    {
                        var postfix = new HarmonyMethod(typeof(Patches.RegionsAndTerritories_EmpiresPatch), nameof(Patches.RegionsAndTerritories_EmpiresPatch.CalculateProductionBase_Postfix));
                        harmony.Patch(originalBase, postfix: postfix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched ResourceFC.CalculateProductionBase successfully.");
                    }

                    var originalMult = AccessTools.Method(resourceFcType, "CalculateProductionMult");
                    if (originalMult != null)
                    {
                        var postfix = new HarmonyMethod(typeof(Patches.RegionsAndTerritories_EmpiresPatch), nameof(Patches.RegionsAndTerritories_EmpiresPatch.CalculateProductionMult_Postfix));
                        harmony.Patch(originalMult, postfix: postfix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched ResourceFC.CalculateProductionMult successfully.");
                    }
                }
                else
                {
                    Log.Message("[RimSynapse-RegionsAndTerritories] Empires mod not detected for ResourceFC. Skipping resource dynamic patching.");
                }

                var rewardDefType = GenTypes.GetTypeInAnyAssembly("FactionColonies.ResourceEventRewardDef");
                if (rewardDefType != null)
                {
                    var originalBuildParams = AccessTools.Method(rewardDefType, "BuildParams");
                    if (originalBuildParams != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patches.RegionsAndTerritories_EmpiresPatch), nameof(Patches.RegionsAndTerritories_EmpiresPatch.BuildParams_Prefix));
                        harmony.Patch(originalBuildParams, prefix: prefix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched ResourceEventRewardDef.BuildParams successfully.");
                    }
                }

                var paymentUtilType = GenTypes.GetTypeInAnyAssembly("FactionColonies.PaymentUtil");
                if (paymentUtilType != null)
                {
                    var originalGenRewards = AccessTools.Method(paymentUtilType, "GenerateRewardThings");
                    if (originalGenRewards != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patches.RegionsAndTerritories_EmpiresPatch), nameof(Patches.RegionsAndTerritories_EmpiresPatch.GenerateRewardThings_Prefix));
                        harmony.Patch(originalGenRewards, prefix: prefix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched PaymentUtil.GenerateRewardThings successfully.");
                    }

                    var originalValOfTithe = AccessTools.Method(paymentUtilType, "ReturnValueOfTithe");
                    if (originalValOfTithe != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patches.RegionsAndTerritories_EmpiresPatch), nameof(Patches.RegionsAndTerritories_EmpiresPatch.ReturnValueOfTithe_Prefix));
                        harmony.Patch(originalValOfTithe, prefix: prefix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched PaymentUtil.ReturnValueOfTithe successfully.");
                    }
                }

                if (settlementMilitaryType != null)
                {
                    var originalSendMilitary = AccessTools.Method(settlementMilitaryType, "SendMilitary", new Type[] { 
                        GenTypes.GetTypeInAnyAssembly("FactionColonies.MercenarySquadFC"),
                        GenTypes.GetTypeInAnyAssembly("FactionColonies.PlanetTile") ?? GenTypes.GetTypeInAnyAssembly("RimWorld.Planet.PlanetTile"),
                        GenTypes.GetTypeInAnyAssembly("FactionColonies.MilitaryJobDef"),
                        typeof(int),
                        typeof(Faction)
                    });

                    if (originalSendMilitary != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patches.RegionsAndTerritories_EmpiresPatch), nameof(Patches.RegionsAndTerritories_EmpiresPatch.SendMilitary_Prefix));
                        harmony.Patch(originalSendMilitary, prefix: prefix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched SettlementMilitary.SendMilitary successfully.");
                    }
                    else
                    {
                        Log.Warning("[RimSynapse-RegionsAndTerritories] Could not find specific SendMilitary method overload in SettlementMilitary.");
                    }
                }

                var checkerType = GenTypes.GetTypeInAnyAssembly("FactionColonies.util.WorldTileChecker");
                var defType = GenTypes.GetTypeInAnyAssembly("FactionColonies.WorldSettlementDef");
                if (checkerType != null && defType != null)
                {
                    var originalIsValid = AccessTools.Method(checkerType, "IsValidTileForNewSettlement", new[] { typeof(RimWorld.Planet.PlanetTile), defType, typeof(System.Text.StringBuilder) });
                    if (originalIsValid != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patches.Patch_WorldTileChecker_IsValidTileForNewSettlement), "Prefix");
                        harmony.Patch(originalIsValid, prefix: prefix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched WorldTileChecker.IsValidTileForNewSettlement successfully.");
                    }
                }

                var debugUtilType = GenTypes.GetTypeInAnyAssembly("FactionColonies.DebugUtil");
                if (debugUtilType != null)
                {
                    var originalCreateTen = AccessTools.Method(debugUtilType, "CreateTenRandomSettlements");
                    if (originalCreateTen != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patches.Patch_DebugUtil_CreateTenRandomSettlements), "Prefix");
                        harmony.Patch(originalCreateTen, prefix: prefix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched DebugUtil.CreateTenRandomSettlements successfully.");
                    }

                    var originalCreatePerResource = AccessTools.Method(debugUtilType, "CreateSettlementPerResource");
                    if (originalCreatePerResource != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patches.Patch_DebugUtil_CreateSettlementPerResource), "Prefix");
                        harmony.Patch(originalCreatePerResource, prefix: prefix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched DebugUtil.CreateSettlementPerResource successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimSynapse-RegionsAndTerritories] Error dynamically patching Empires: {ex.Message}");
            }
        }

        private void TryPatchVOE(Harmony harmony)
        {
            try
            {
                var type = GenTypes.GetTypeInAnyAssembly("Outposts.Utils");
                if (type != null)
                {
                    var target = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "CanSpawnOnWithExt");
                    if (target != null)
                    {
                        var postfix = new HarmonyMethod(typeof(Patches.RegionsAndTerritories_EmpiresPatch), nameof(Patches.RegionsAndTerritories_EmpiresPatch.VOE_CanSpawnOnWithExt_Postfix));
                        harmony.Patch(target, postfix: postfix);
                        Log.Message("[RimSynapse-RegionsAndTerritories] Dynamically patched Outposts.Utils.CanSpawnOnWithExt successfully.");
                    }
                    else
                    {
                        Log.Warning("[RimSynapse-RegionsAndTerritories] Could not find CanSpawnOnWithExt method in Outposts.Utils.");
                    }
                }
                else
                {
                    Log.Message("[RimSynapse-RegionsAndTerritories] Vanilla Outposts Expanded not detected. Skipping VOE dynamic patching.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimSynapse-RegionsAndTerritories] Error dynamically patching VOE: {ex.Message}");
            }
        }
    }
}
