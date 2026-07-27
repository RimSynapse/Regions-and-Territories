using Verse;

namespace RimSynapse.RegionsAndTerritories.Integration
{
    /// <summary>
    /// Player-facing switches for the 0.7 world-object governance layer.
    ///
    /// Everything here defaults to ON when the backing mod is present, and every switch is a pure
    /// no-op path when off — that is the "optional additions" requirement for 0.7. Persisted by
    /// <see cref="FactionPlacementSettings"/>, which owns the mod's ModSettings instance.
    /// </summary>
    public static class WorldObjectIntegrationSettings
    {
        /// <summary>Master switch. Off means R&amp;T governs only vanilla objects, as before 0.7.</summary>
        public static bool masterEnabled = true;

        // --- Per-mod integrations -------------------------------------------------
        public static bool empireEnabled = true;
        public static bool voeEnabled = true;
        public static bool vfeEnabled = true;
        public static bool worldDominationEnabled = true;

        // --- Per-mechanic switches ------------------------------------------------
        /// <summary>Gate placement of foreign world objects on region ownership and supply range.</summary>
        public static bool placementGovernance = true;

        /// <summary>Apply security/ownership, resource-cap, and local-richness modifiers to production.</summary>
        public static bool economyGovernance = true;

        /// <summary>Apply adjacency and supply-line restrictions to military and expansion actions.</summary>
        public static bool militaryGovernance = true;

        /// <summary>Classify settlements into village/town/city/major-city tiers.</summary>
        public static bool settlementTiers = true;

        // --- Diagnostics ----------------------------------------------------------
        /// <summary>Log each world-object type that no adapter or heuristic could classify (once per type).</summary>
        public static bool logUnknownWorldObjects = true;

        public static void ExposeData()
        {
            Scribe_Values.Look(ref masterEnabled, "integration_masterEnabled", true);
            Scribe_Values.Look(ref empireEnabled, "integration_empireEnabled", true);
            Scribe_Values.Look(ref voeEnabled, "integration_voeEnabled", true);
            Scribe_Values.Look(ref vfeEnabled, "integration_vfeEnabled", true);
            Scribe_Values.Look(ref worldDominationEnabled, "integration_worldDominationEnabled", true);

            Scribe_Values.Look(ref placementGovernance, "integration_placementGovernance", true);
            Scribe_Values.Look(ref economyGovernance, "integration_economyGovernance", true);
            Scribe_Values.Look(ref militaryGovernance, "integration_militaryGovernance", true);
            Scribe_Values.Look(ref settlementTiers, "integration_settlementTiers", true);

            Scribe_Values.Look(ref logUnknownWorldObjects, "integration_logUnknownWorldObjects", true);
        }

        // Convenience accessors so call sites read as intent rather than as boolean algebra.

        public static bool PlacementGovernanceActive
        {
            get { return masterEnabled && placementGovernance; }
        }

        public static bool EconomyGovernanceActive
        {
            get { return masterEnabled && economyGovernance; }
        }

        public static bool MilitaryGovernanceActive
        {
            get { return masterEnabled && militaryGovernance; }
        }

        public static bool SettlementTiersActive
        {
            get { return masterEnabled && settlementTiers; }
        }
    }
}
