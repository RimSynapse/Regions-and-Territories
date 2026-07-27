using RimSynapse.RegionsAndTerritories.Integration;

namespace RimSynapse.RegionsAndTerritories.Placement
{
    /// <summary>
    /// The numeric rules governing where a world object may be placed, in one table.
    ///
    /// Every value here was previously a magic number inside <c>OutpostPlacementUtility</c> or
    /// <c>RegionalOwnershipUtility</c>. Naming them is a precondition for 0.8 (Logic
    /// Externalization), which lifts these out of code entirely.
    ///
    /// The defaults are chosen so that a world containing only settlements and outposts behaves
    /// exactly as it did before 0.7 — the rules only start to differ once camps and military
    /// installations exist, which is precisely what Epic 2 set out to govern.
    /// </summary>
    public static class PlacementRules
    {
        /// <summary>
        /// Maximum tiles a new permanent holding may sit from the placing faction's nearest
        /// holding or territory border. Was <c>OutpostPlacementUtility.MAX_SUPPLY_DISTANCE</c>.
        /// </summary>
        public const int MaxSupplyDistance = 8;

        /// <summary>
        /// Minimum traversal distance between two permanent holdings. 2 means "at least one empty
        /// tile between them". Was <c>OutpostPlacementUtility.MIN_BUFFER_DISTANCE</c>.
        /// </summary>
        public const int PermanentHoldingSeparation = 2;

        /// <summary>
        /// Share of a province a faction must score before it is treated as holding territory
        /// there. Was the bare <c>0.30f</c> in <c>GetDistanceToFactionBorder</c>.
        /// </summary>
        public const float OwnershipThreshold = 0.30f;

        /// <summary>
        /// How close the runner-up's score must be to the leader's before the province counts as
        /// contested rather than owned. Both must still clear <see cref="OwnershipThreshold"/>.
        /// </summary>
        public const float ContestMargin = 0.10f;

        /// <summary>
        /// Minimum distance required between an object of kind <paramref name="a"/> and an
        /// existing object of kind <paramref name="b"/>. 0 means the pair is exempt.
        ///
        /// Camps are expeditionary and deliberately exempt: a raiding camp pitched next to the
        /// settlement it is besieging is the point of a camp, not a rules violation.
        /// </summary>
        public static int MinSeparation(WorldObjectKind a, WorldObjectKind b)
        {
            if (a.IsPermanentHolding() && b.IsPermanentHolding())
            {
                return PermanentHoldingSeparation;
            }
            return 0;
        }

        /// <summary>
        /// Whether a new object of this kind has to be within supply range of its faction.
        /// Permanent holdings do; camps do not.
        /// </summary>
        public static bool RequiresSupplyLine(WorldObjectKind kind)
        {
            return kind.IsPermanentHolding();
        }

        /// <summary>
        /// Whether a new object of this kind has to be in or beside a province its faction already
        /// holds (the sequential-expansion rule the vanilla settle path has always enforced).
        /// </summary>
        public static bool RequiresAdjacentFoothold(WorldObjectKind kind)
        {
            return kind.IsPermanentHolding();
        }

        /// <summary>
        /// Whether foreign ownership of a province blocks this kind outright. Permanent holdings
        /// are an act of settlement and are refused; camps are transient and merely trespass.
        /// </summary>
        public static bool BlockedByForeignTerritory(WorldObjectKind kind)
        {
            return kind.IsPermanentHolding();
        }
    }
}
