using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSynapse.RegionsAndTerritories
{
    public class RegionsAndTerritoriesMod : Mod
    {
        public static FactionPlacementSettings Settings;

        public override string SettingsCategory() => "RimSynapse Regions & Territories";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);

            l.CheckboxLabeled("Show ownership calculation breakdown in the region panel",
                ref FactionPlacementSettings.showCalculationBreakdowns,
                "Adds the developer ownership-derivation readout to the expanded region panel (opened with the modifier + click chosen below). Off by default, and never shown in the hover tooltip.");

            l.Gap();
            l.Label("Open a region's comparison panel with:");
            if (l.RadioButton("Ctrl + click", !FactionPlacementSettings.regionPanelUseShift))
            {
                FactionPlacementSettings.regionPanelUseShift = false;
            }
            if (l.RadioButton("Shift + click", FactionPlacementSettings.regionPanelUseShift))
            {
                FactionPlacementSettings.regionPanelUseShift = true;
            }

            l.Gap();
            FactionPlacementSettings.maxRegionPanels = Mathf.RoundToInt(l.SliderLabeled(
                $"Max comparison panels open at once: {FactionPlacementSettings.maxRegionPanels}",
                FactionPlacementSettings.maxRegionPanels, 1f, 8f));

            l.Gap();
            l.Label("Planet region size, placement rules and world-object integration are configured on the world-generation screen.");
            l.End();
        }

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

            // 0.7: build the mod-agnostic world-object adapter set before any integration patching,
            // so the patches below can classify through the registry instead of naming mod types.
            Integration.WorldObjectAdapterRegistry.Initialize();

            RegisterProvidersWithCore();

            // Region introspection tools over Core's MCP bridge (get_region_info, show_world_map).
            // Deferred so Core has registered its own tools first — both run via ExecuteWhenFinished
            // and Core loads before this mod, so its callback is queued first.
            LongEventHandler.ExecuteWhenFinished(Integration.RegionMcpTools.RegisterWithCore);

            TryPatchEmpires(harmony);
            TryPatchVOE(harmony);
        }

        /// <summary>
        /// Publish the capabilities this mod owns to RimSynapse Core, if Core is installed.
        ///
        /// <para>All by reflection, and this mod holds no assembly reference to Core — it has to
        /// build and run on its own, with nothing but Map Mode Framework. Every branch logs,
        /// because a provider that quietly failed to register is indistinguishable from one
        /// answering "nothing", which is the same failure class as an unbound Harmony patch.</para>
        /// </summary>
        private void RegisterProvidersWithCore()
        {
            var providers = GenTypes.GetTypeInAnyAssembly("RimSynapse.SynapseCoreProviders");
            if (providers == null)
            {
                // Either Core is absent, or it predates the provider registry. Fall back so an
                // older Core still gets population density.
                TryRegisterLegacyPopulationDelegate();
                return;
            }

            TryRegisterProvider(providers, "PopulationDensity",
                (Func<int, int>)PopulationDensityUtility.GetPopulationAtTile);

            Residency.ResidencyUtility.RegisterWithCore();
        }

        private void TryRegisterProvider(Type providers, string slotName, Delegate provider)
        {
            try
            {
                var slot = providers.GetProperty(slotName, BindingFlags.Public | BindingFlags.Static);
                if (slot == null || !slot.CanWrite)
                {
                    Log.Warning($"[RimSynapse-RegionsAndTerritories] SynapseCoreProviders has no writable '{slotName}' slot; that capability will not be visible to other mods.");
                    return;
                }

                slot.SetValue(null, provider);
                Log.Message($"[RimSynapse-RegionsAndTerritories] Registered '{slotName}' provider to RimSynapse Core successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimSynapse-RegionsAndTerritories] Error registering '{slotName}' provider: {ex}");
            }
        }

        /// <summary>
        /// The pre-registry registration path, for a copy of Core older than the provider surface.
        /// Remove when that Core's shim field goes.
        /// </summary>
        private void TryRegisterLegacyPopulationDelegate()
        {
            try
            {
                var coreWorldCompType = GenTypes.GetTypeInAnyAssembly("RimSynapse.SynapseCoreWorldComponent");
                if (coreWorldCompType == null)
                {
                    Log.Message("[RimSynapse-RegionsAndTerritories] RimSynapse Core not detected. Running standalone; no providers registered.");
                    return;
                }

                var field = coreWorldCompType.GetField("GetPopulationDensityDelegate", BindingFlags.Public | BindingFlags.Static);
                if (field != null)
                {
                    Func<int, int> del = PopulationDensityUtility.GetPopulationAtTile;
                    field.SetValue(null, del);
                    Log.Message("[RimSynapse-RegionsAndTerritories] Registered population delegate to RimSynapse Core (legacy field) successfully.");
                }
                else
                {
                    Log.Warning("[RimSynapse-RegionsAndTerritories] Could not find GetPopulationDensityDelegate field in SynapseCoreWorldComponent.");
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
