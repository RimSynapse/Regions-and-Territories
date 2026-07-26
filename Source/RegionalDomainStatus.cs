using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimSynapse.RegionsAndTerritories
{
    public enum ProvinceDomainStatus
    {
        Wilderness,
        DominantOwner,
        Contested,
        Conflict
    }

    public static class RegionalDomainUtility
    {
        public static ProvinceDomainStatus GetDomainStatus(RegionalOwnershipData data)
        {
            if (data == null || data.factionScores == null || data.factionScores.Count == 0)
            {
                return ProvinceDomainStatus.Wilderness;
            }

            if (data.factionScores.Any(s => s.TotalScore > 0.70f))
            {
                return ProvinceDomainStatus.DominantOwner;
            }

            int contenders = data.factionScores.Count(s => s.TotalScore >= 0.30f);
            if (contenders >= 2)
            {
                return ProvinceDomainStatus.Conflict;
            }
            if (contenders == 1)
            {
                return ProvinceDomainStatus.Contested;
            }

            return ProvinceDomainStatus.Wilderness;
        }

        public static Faction GetDominantOwner(RegionalOwnershipData data)
        {
            return data?.factionScores?.FirstOrDefault(s => s.TotalScore > 0.70f)?.faction;
        }

        public static List<Faction> GetContestedFactions(RegionalOwnershipData data)
        {
            return data?.factionScores?.Where(s => s.TotalScore >= 0.30f).Select(s => s.faction).ToList() ?? new List<Faction>();
        }

        public static string GetStatusDescription(RegionalOwnershipData data)
        {
            ProvinceDomainStatus status = GetDomainStatus(data);
            switch (status)
            {
                case ProvinceDomainStatus.DominantOwner:
                    Faction dominant = GetDominantOwner(data);
                    return "Domain of: " + (dominant != null ? TextureUtility.GetFactionDisplayName(dominant) : "Unknown Faction") + " (>70% Influence)";
                case ProvinceDomainStatus.Conflict:
                    var contenders = GetContestedFactions(data).Select(f => TextureUtility.GetFactionDisplayName(f));
                    return "Contested Conflict: " + string.Join(" vs ", contenders) + " (>=30% Influence)";
                case ProvinceDomainStatus.Contested:
                    var topContender = GetContestedFactions(data).FirstOrDefault();
                    string topName = topContender != null ? TextureUtility.GetFactionDisplayName(topContender) : "Unknown";
                    return "Contested Territory: " + topName + " (30%-70% Influence)";
                default:
                    return "Unaffiliated Wilderness (<30% Faction Influence)";
            }
        }
    }
}
