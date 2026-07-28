using System.Collections.Generic;

namespace RimSynapse.RegionsAndTerritories.Integration
{
    /// <summary>
    /// The declarative table of mods Regions &amp; Territories knows how to govern.
    ///
    /// Adding support for "another mod that acts like Empire Refactored" should be an entry here.
    /// Nothing outside this file and the adapters may name a foreign type.
    /// </summary>
    public static class KnownModProfiles
    {
        public static List<WorldObjectAdapterProfile> All()
        {
            var list = new List<WorldObjectAdapterProfile>();
            list.Add(Empire());
            list.Add(VanillaOutpostsExpanded());
            list.Add(VanillaFactionsExpanded());
            list.Add(WorldDomination());
            return list;
        }

        /// <summary>
        /// Empire Refactored (packageId Matathias.Empire, assembly namespace FactionColonies).
        /// Settlements are WorldSettlementFC, which derives from vanilla Settlement.
        /// </summary>
        public static WorldObjectAdapterProfile Empire()
        {
            var p = new WorldObjectAdapterProfile
            {
                adapterId = "empire",
                displayName = "Empire Refactored",
                priority = 100,
                markerTypes = new[]
                {
                    "FactionColonies.FindFC",
                    "FactionColonies.WorldSettlementFC"
                },
                // SettlementFC exposes settlementLevel; the world object holds a reference to it.
                populationMembers = new[] { "population", "Population", "settlementPopulation" },
                levelMembers = new[] { "settlementLevel", "SettlementLevel", "level" },
                maxLevelMembers = new[] { "maxSettlementLevel" },
                assumedMaxLevel = 10,
                // Empire runs the player's own colonies as WorldSettlementFC objects.
                playerOwnedByDefault = true,
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && WorldObjectIntegrationSettings.empireEnabled
            };

            p.Rule(TypeMatch.ExactType, "FactionColonies.WorldSettlementFC", WorldObjectKind.Settlement)
             .Rule(TypeMatch.NamespacePrefix, "FactionColonies.WorldSettlement", WorldObjectKind.Settlement)
             .Rule(TypeMatch.TypeNameContains, "MilitaryFC", WorldObjectKind.Military);

            return p;
        }

        /// <summary>
        /// Vanilla Outposts Expanded (packageId vanillaexpanded.outposts, namespace Outposts).
        /// All outposts derive from Outposts.Outpost; PawnCount is the resident count.
        /// </summary>
        public static WorldObjectAdapterProfile VanillaOutpostsExpanded()
        {
            var p = new WorldObjectAdapterProfile
            {
                adapterId = "voe",
                displayName = "Vanilla Outposts Expanded",
                priority = 110,
                markerTypes = new[] { "Outposts.Outpost" },
                populationMembers = new[] { "PawnCount", "occupants" },
                levelMembers = new[] { "level", "Level", "upgradeLevel" },
                assumedMaxLevel = 0,
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && WorldObjectIntegrationSettings.voeEnabled
            };

            p.Rule(TypeMatch.ExactType, "Outposts.Outpost", WorldObjectKind.Outpost)
             .Rule(TypeMatch.NamespacePrefix, "Outposts.", WorldObjectKind.Outpost);

            return p;
        }

        /// <summary>
        /// Vanilla Factions Expanded family. VFE mods share the VFECore / VanillaFactionsExpanded
        /// namespaces and add settlement-like and camp-like world objects.
        /// </summary>
        public static WorldObjectAdapterProfile VanillaFactionsExpanded()
        {
            var p = new WorldObjectAdapterProfile
            {
                adapterId = "vfe",
                displayName = "Vanilla Factions Expanded",
                priority = 120,
                markerTypes = new[]
                {
                    "VFECore.VFECore",
                    "VFECore.SettlementDefExtension",
                    "VanillaFactionsExpanded.VanillaFactionsExpandedMod"
                },
                populationMembers = new[] { "PawnCount", "population" },
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && WorldObjectIntegrationSettings.vfeEnabled
            };

            p.Rule(TypeMatch.TypeNameContains, "Settlement", WorldObjectKind.Settlement)
             .Rule(TypeMatch.TypeNameContains, "Camp", WorldObjectKind.Camp)
             .Rule(TypeMatch.TypeNameContains, "Outpost", WorldObjectKind.Outpost)
             .Rule(TypeMatch.TypeNameContains, "Base", WorldObjectKind.Military);

            return p;
        }

        /// <summary>
        /// World Domination (LikewiseHH). It declares Vanilla Outposts Expanded as a dependency and
        /// layers tiered, upgradeable faction bases on top of it, so most of its objects should
        /// already resolve through the VOE adapter (priority 110 runs first).
        ///
        /// This profile is deliberately heuristic: the exact namespace has not been pinned against a
        /// live install yet. <see cref="WorldObjectClassifier"/> logs every world-object type it fails
        /// to classify, so pinning it is a one-line change once the mod is loaded in a test run.
        /// </summary>
        public static WorldObjectAdapterProfile WorldDomination()
        {
            var p = new WorldObjectAdapterProfile
            {
                adapterId = "worlddomination",
                displayName = "World Domination",
                priority = 130,
                markerTypes = new[]
                {
                    "WorldDomination.WorldDominationMod",
                    "WorldDomination.WorldDominationSettings"
                },
                populationMembers = new[] { "PawnCount", "population", "occupants", "garrison" },
                levelMembers = new[] { "tier", "Tier", "level", "Level" },
                assumedMaxLevel = 0,
                enabledGetter = () => WorldObjectIntegrationSettings.masterEnabled && WorldObjectIntegrationSettings.worldDominationEnabled
            };

            p.Rule(TypeMatch.NamespacePrefix, "WorldDomination.", WorldObjectKind.Settlement)
             .Rule(TypeMatch.TypeNameContains, "Garrison", WorldObjectKind.Military)
             .Rule(TypeMatch.TypeNameContains, "Territory", WorldObjectKind.Settlement);

            return p;
        }
    }
}
