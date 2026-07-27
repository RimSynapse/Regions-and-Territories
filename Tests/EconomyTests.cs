// Behaviour tests for the 0.7 regional resource model (Epic 3, pure core).
//
// ResourceEvaluator is pure, so this suite needs no RimWorld. What is being checked is that stock
// can be drawn down and recovered without ever escaping its bounds, that a faction which cannot
// scan genuinely cannot recover minerals, that the sustainable-population figure the model derives
// actually is the break-even point, and that none of it can silently destroy a province.
using System;
using RimSynapse.RegionsAndTerritories.Economy;
using RimSynapse.RegionsAndTerritories.Sizing;

namespace EconomyTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("a pool is bounded at both ends");
            var pool = new ResourcePool(1000f);
            Check("a new pool starts full", Near(pool.current, 1000f));
            Check("drawing returns what was taken", Near(pool.Draw(300f), 300f));
            Check("the stock falls by what was taken", Near(pool.current, 700f));
            Check("growth cannot exceed the cap", Near(pool.Grow(9999f), 300f) && Near(pool.current, 1000f));
            Check("a full pool accepts no more", Near(pool.Grow(50f), 0f));
            Check("drawing more than exists returns only what exists", Near(pool.Draw(5000f), 1000f));
            Check("the stock floors at zero", Near(pool.current, 0f));
            Check("an empty pool reads as exhausted", pool.IsExhausted);
            Check("drawing from an empty pool yields nothing", Near(pool.Draw(100f), 0f));
            Check("a negative draw is not a deposit", Near(pool.Draw(-500f), 0f) && Near(pool.current, 0f));
            Check("a negative growth is not a withdrawal", Near(pool.Grow(-500f), 0f) && Near(pool.current, 0f));

            Section("a 0.6 save loads as a full province, not an empty one");
            // Pre-0.7 provinces stored one number, which was a full stock. Seeding must read it that
            // way: getting this backwards would hand every existing save a world with no resources.
            var legacy = new ResourcePool { cap = 800f, current = ResourcePool.Unseeded };
            legacy.EnsureSeeded();
            Check("an unseeded pool fills from its cap", Near(legacy.current, 800f));
            legacy.Draw(800f);
            legacy.EnsureSeeded();
            Check("seeding does not resurrect a mined-out province", Near(legacy.current, 0f));
            var overfull = new ResourcePool { cap = 100f, current = 5000f };
            overfull.EnsureSeeded();
            Check("seeding clamps a stock above its cap", Near(overfull.current, 100f));

            Section("an empty cap is not a starving province");
            // Fraction is read by UI and by production scaling. A province with no cap for a
            // resource has nothing missing, so it must read full rather than as a shortage.
            var barren = new ResourcePool(0f);
            Check("a zero cap reads as full", Near(barren.Fraction, 1f));
            Check("a zero cap is not exhausted", !barren.IsExhausted);

            Section("changing the cap never inflates the stock");
            var shrinking = new ResourcePool(1000f);
            shrinking.SetCap(400f);
            Check("shrinking the cap clamps the stock", Near(shrinking.current, 400f));
            shrinking.SetCap(2000f);
            Check("growing the cap leaves the stock where it was", Near(shrinking.current, 400f));
            shrinking.SetCap(-50f);
            Check("a negative cap is treated as none", Near(shrinking.cap, 0f));

            Section("scanning has to be earned");
            Check("an illiterate operator scans nothing", Near(ResourceEvaluator.Competence(0f), 0f));
            Check("a below-threshold skill scans nothing", Near(ResourceEvaluator.Competence(8f), 0f));
            Check("a top researcher is fully competent", Near(ResourceEvaluator.Competence(20f), 1f));
            Check("competence rises across the band",
                ResourceEvaluator.Competence(10f) > 0f && ResourceEvaluator.Competence(10f) < ResourceEvaluator.Competence(14f));

            Check("no research means no scanning however clever the pawn",
                Near(ResourceEvaluator.ScanCapability(0f, 1f), 0f));
            Check("no competent pawn means no scanning however advanced the faction",
                Near(ResourceEvaluator.ScanCapability(1f, 0f), 0f));
            Check("a fully researched faction with a top researcher scans at full rate",
                Near(ResourceEvaluator.ScanCapability(1f, 1f), 1f));
            Check("scanning capability never exceeds one",
                ResourceEvaluator.ScanCapability(5f, 1f) <= 1f);
            Check("scanning capability is never negative",
                ResourceEvaluator.ScanCapability(-1f, -1f) >= 0f);

            Section("renewal depends on what the resource is");
            Check("forests regrow without anyone's help",
                ResourceEvaluator.RenewalPerYear(ResourceKind.Biomass, 1000f, 0f) > 0f);
            Check("nutrition regrows without anyone's help",
                ResourceEvaluator.RenewalPerYear(ResourceKind.Nutrition, 1000f, 0f) > 0f);
            Check("biological renewal ignores scanning",
                Near(ResourceEvaluator.RenewalPerYear(ResourceKind.Biomass, 1000f, 0f),
                     ResourceEvaluator.RenewalPerYear(ResourceKind.Biomass, 1000f, 1f)));

            Check("minerals recover nothing without scanning",
                Near(ResourceEvaluator.RenewalPerYear(ResourceKind.Minerals, 1000f, 0f), 0f));
            Check("minerals recover with scanning",
                ResourceEvaluator.RenewalPerYear(ResourceKind.Minerals, 1000f, 1f) > 0f);
            Check("mineral recovery scales with scanning capability",
                ResourceEvaluator.RenewalPerYear(ResourceKind.Minerals, 1000f, 0.5f) <
                ResourceEvaluator.RenewalPerYear(ResourceKind.Minerals, 1000f, 1f));
            Check("minerals recover far slower than forests",
                ResourceEvaluator.RenewalPerYear(ResourceKind.Minerals, 1000f, 1f) <
                ResourceEvaluator.RenewalPerYear(ResourceKind.Biomass, 1000f, 1f));

            Check("manufactured goods have no natural pool",
                Near(ResourceEvaluator.RenewalPerYear(ResourceKind.IndustrialGoods, 1000f, 1f), 0f));
            Check("a province with no cap renews nothing",
                Near(ResourceEvaluator.RenewalPerYear(ResourceKind.Biomass, 0f, 1f), 0f));

            Section("resource classification");
            Check("minerals are geological", ResourceKind.Minerals.Renewal() == RenewalClass.Geological);
            Check("biomass is biological", ResourceKind.Biomass.Renewal() == RenewalClass.Biological);
            Check("textiles are biological", ResourceKind.Textiles.Renewal() == RenewalClass.Biological);
            Check("spacer goods are manufactured", ResourceKind.SpacerGoods.Renewal() == RenewalClass.Manufactured);
            Check("goods are not extracted", !ResourceKind.PreIndustrialGoods.IsExtracted());
            Check("minerals are extracted", ResourceKind.Minerals.IsExtracted());
            bool labelled = true;
            foreach (ResourceKind k in Enum.GetValues(typeof(ResourceKind)))
            {
                if (string.IsNullOrEmpty(k.Label())) labelled = false;
            }
            Check("every resource has a label", labelled);

            Section("bigger settlements consume faster");
            Check("nobody extracts nothing", Near(ResourceEvaluator.ExtractionPerYear(0, 1f), 0f));
            Check("extraction rises with population",
                ResourceEvaluator.ExtractionPerYear(50, 1f) < ResourceEvaluator.ExtractionPerYear(100, 1f));
            Check("a major city draws harder than a village at the same headcount",
                ResourceEvaluator.ExtractionPerYear(100, SettlementSizeRules.ProductionScale(SettlementTier.Village)) <
                ResourceEvaluator.ExtractionPerYear(100, SettlementSizeRules.ProductionScale(SettlementTier.MajorCity)));
            Check("an untiered holding still extracts at the neutral rate",
                Near(ResourceEvaluator.ExtractionPerYear(100, SettlementSizeRules.ProductionScale(SettlementTier.None)),
                     ResourceEvaluator.ExtractionPerYear(100, 1f)));

            Section("the derived sustainable population really is break-even");
            // The design note asked for a spacer population threshold. Rather than pick one, the
            // model derives it - so the thing to test is that the number it derives is actually the
            // point where the province stops losing ground.
            float cap = 10000f;
            float scan = ResourceEvaluator.ScanCapability(1f, 1f);
            int threshold = ResourceEvaluator.SustainablePopulation(ResourceKind.Minerals, cap, scan, 1f);
            Check("a fully-scanning faction has a positive sustainable population", threshold > 0);
            Check("at the threshold the province holds",
                ResourceEvaluator.IsSustainable(ResourceKind.Minerals, cap, scan, threshold, 1f));
            Check("just above the threshold the province declines",
                !ResourceEvaluator.IsSustainable(ResourceKind.Minerals, cap, scan, threshold + 2, 1f));

            Check("a faction that cannot scan sustains nobody on minerals",
                ResourceEvaluator.SustainablePopulation(ResourceKind.Minerals, cap, 0f, 1f) == 0);
            Check("but the same faction sustains a population on forest",
                ResourceEvaluator.SustainablePopulation(ResourceKind.Biomass, cap, 0f, 1f) > 0);
            Check("manufactured goods sustain nobody",
                ResourceEvaluator.SustainablePopulation(ResourceKind.SpacerGoods, cap, 1f, 1f) == 0);

            Check("a richer province sustains more people",
                ResourceEvaluator.SustainablePopulation(ResourceKind.Minerals, 20000f, scan, 1f) >
                ResourceEvaluator.SustainablePopulation(ResourceKind.Minerals, 10000f, scan, 1f));
            Check("a larger settlement sustains fewer people on the same ground",
                ResourceEvaluator.SustainablePopulation(ResourceKind.Minerals, cap, scan,
                    SettlementSizeRules.ProductionScale(SettlementTier.MajorCity)) <
                ResourceEvaluator.SustainablePopulation(ResourceKind.Minerals, cap, scan,
                    SettlementSizeRules.ProductionScale(SettlementTier.Village)));

            Section("advancing a province over time");
            var mine = new ResourcePool(10000f);
            float taken = ResourceEvaluator.Advance(mine, ResourceKind.Minerals, 0f, 100, 1f, 1f);
            Check("a year of extraction takes something", taken > 0f);
            Check("the stock fell by what was taken", Near(mine.current, 10000f - taken));

            var untouched = new ResourcePool(10000f);
            ResourceEvaluator.Advance(untouched, ResourceKind.Minerals, 0f, 0, 1f, 1f);
            Check("an unpopulated province with no scanning is left exactly alone",
                Near(untouched.current, 10000f));

            var forest = new ResourcePool(10000f);
            forest.Draw(5000f);
            ResourceEvaluator.Advance(forest, ResourceKind.Biomass, 0f, 0, 1f, 1f);
            Check("an unpopulated forest regrows", forest.current > 5000f);

            Section("depletion is a curve, not a cliff");
            // Without an annual draw ceiling, one economy tick with a large population empties any
            // province outright and the player never sees it coming.
            var swarmed = new ResourcePool(10000f);
            ResourceEvaluator.Advance(swarmed, ResourceKind.Minerals, 0f, 1000000, 1f, 1f);
            Check("an absurd population cannot empty a province in one year", swarmed.current > 0f);
            Check("but it takes a serious bite", swarmed.current < 10000f);

            Section("a province cannot be destroyed by advancing it");
            var abused = new ResourcePool(10000f);
            bool bounded = true;
            for (int year = 0; year < 200; year++)
            {
                ResourceEvaluator.Advance(abused, ResourceKind.Minerals, 0.4f, 250, 1.75f, 1f);
                if (abused.current < 0f || abused.current > abused.cap) bounded = false;
            }
            Check("two centuries of hard use keeps the stock in bounds", bounded);
            Check("hard use over two centuries does deplete the mine", abused.current < 10000f);

            var recovering = new ResourcePool(10000f);
            recovering.Draw(10000f);
            for (int year = 0; year < 500; year++)
            {
                ResourceEvaluator.Advance(recovering, ResourceKind.Minerals, 1f, 0, 1f, 1f);
            }
            Check("an exhausted mine with full scanning eventually refills", Near(recovering.current, 10000f));
            Check("and never overfills", recovering.current <= recovering.cap);

            Section("goods are left alone by the extraction model");
            var goods = new ResourcePool(500f);
            float fromGoods = ResourceEvaluator.Advance(goods, ResourceKind.IndustrialGoods, 1f, 5000, 2.25f, 10f);
            Check("manufactured goods are not extracted by this model", Near(fromGoods, 0f));
            Check("and are not touched at all", Near(goods.current, 500f));

            Section("ticks and years agree");
            var byYear = new ResourcePool(10000f);
            var byTicks = new ResourcePool(10000f);
            ResourceEvaluator.Advance(byYear, ResourceKind.Minerals, 0.5f, 200, 1f, 1f);
            ResourceEvaluator.AdvanceTicks(byTicks, ResourceKind.Minerals, 0.5f, 200, 1f, ResourceRules.TicksPerYear);
            Check("a year of ticks matches a year", Near(byYear.current, byTicks.current, 0.5f));
            Check("zero ticks change nothing",
                Near(ResourceEvaluator.AdvanceTicks(new ResourcePool(100f), ResourceKind.Minerals, 1f, 10, 1f, 0), 0f));
            Check("a null pool is survivable",
                Near(ResourceEvaluator.Advance(null, ResourceKind.Minerals, 1f, 10, 1f, 1f), 0f));

            // The player-facing line lives here rather than in MapMode_GeographicProvinces because
            // that file references MapModeFramework, which has no stub, so it is the one piece of
            // Epic 3 that cannot be compiled in this sandbox. Keeping the wording out of it means
            // the part that can be wrong is the part that is tested.
            Section("how a pool reads to the player");
            Check("terrain that holds nothing says so, not '0 / 0'",
                ResourceDisplay.Line(new ResourcePool(0f), "Minerals") == "Minerals: none");
            Check("a null pool degrades to the same line",
                ResourceDisplay.Line(null, "Minerals") == "Minerals: none");

            var pristine = new ResourcePool(1200f);
            Check("an untouched province shows a bare number, no percentage",
                ResourceDisplay.Line(pristine, "Biomass") == "Biomass: 1200");

            var worked = new ResourcePool(1000f);
            worked.Draw(400f);
            string workedLine = ResourceDisplay.Line(worked, "Nutrition");
            Check("a worked province shows stock against ceiling",
                workedLine.StartsWith("Nutrition: 600 / 1000 ("));
            Check("and names how far down it has been drawn", workedLine.Contains("60"));

            var stripped = new ResourcePool(1000f);
            stripped.Draw(1000f);
            Check("a mined-out province says what it used to hold",
                ResourceDisplay.Line(stripped, "Minerals") == "Minerals: exhausted (was 1000)");
            Check("which is a different sentence from having never held any",
                ResourceDisplay.Line(stripped, "Minerals") != ResourceDisplay.Line(new ResourcePool(0f), "Minerals"));

            var barelyTouched = new ResourcePool(100000f);
            barelyTouched.Draw(1f);
            Check("a rounding-error draw still reads as full",
                ResourceDisplay.Line(barelyTouched, "Textiles") == "Textiles: 100000");

            // --- Production model (Epic 3 children 1, 3, 4) ---------------------------------
            //
            // The load-bearing property is the last section: with no new data, the model must
            // reproduce 0.6's arithmetic exactly. Everything above it is the new behaviour that
            // only appears once a caller actually has something to say.

            Section("abundance reproduces the 0.6 resource scale");
            Check("a province at its baseline is neutral",
                Near(ProductionEvaluator.AbundanceFactor(500f, 500f), 1f));
            Check("twice the baseline doubles output",
                Near(ProductionEvaluator.AbundanceFactor(1000f, 500f), 2f));
            Check("but the bonus is capped there",
                Near(ProductionEvaluator.AbundanceFactor(50000f, 500f), 2f));
            Check("a poor province floors rather than collapsing",
                Near(ProductionEvaluator.AbundanceFactor(1f, 500f), 0.2f));
            Check("an empty province still produces something",
                Near(ProductionEvaluator.AbundanceFactor(0f, 500f), 0.2f));
            Check("a null pool is survivable",
                Near(ProductionEvaluator.AbundanceFactor(null, ResourceKind.Minerals), 0.2f));

            // This is the depletion feedback loop closing: the same province, worked down, yields
            // less. Nothing before 0.7 could express this, because there was no "worked down".
            var seam = new ResourcePool(1000f);
            float rich = ProductionEvaluator.AbundanceFactor(seam, ResourceKind.Minerals);
            seam.Draw(800f);
            float spent = ProductionEvaluator.AbundanceFactor(seam, ResourceKind.Minerals);
            Check("working a province down reduces what it yields", spent < rich);
            Check("and a full one yields more than a poor one", rich > 1f && spent < 1f);

            Section("labour reproduces the 0.6 population curve");
            Check("an empty province sits below baseline", Near(ProductionEvaluator.LabourFactor(0), 0.8f));
            Check("population lifts it", Near(ProductionEvaluator.LabourFactor(1000), 1.3f));
            Check("and it caps", Near(ProductionEvaluator.LabourFactor(999999), 1.5f));
            Check("a negative headcount is treated as empty", Near(ProductionEvaluator.LabourFactor(-50), 0.8f));

            Section("security is a bonus for holding ground, never a tax");
            Check("owning nothing changes nothing", Near(ProductionEvaluator.SecurityFactor(0f, 1f), 1f));
            Check("owning nothing and policing nothing still changes nothing",
                Near(ProductionEvaluator.SecurityFactor(0f, 0f), 1f));
            Check("ownership below the placement threshold earns nothing",
                Near(ProductionEvaluator.SecurityFactor(0.29f, 1f), 1f));
            Check("full ownership fully secured earns the whole bonus",
                Near(ProductionEvaluator.SecurityFactor(1f, 1f), 1.25f));
            Check("the same territory unpoliced earns none of it",
                Near(ProductionEvaluator.SecurityFactor(1f, 0f), 1f));
            Check("policing ground you do not own earns nothing",
                Near(ProductionEvaluator.SecurityFactor(0.1f, 1f), 1f));
            Check("half-held and half-policed earns less than either alone would suggest",
                ProductionEvaluator.SecurityFactor(0.65f, 0.5f) < ProductionEvaluator.SecurityFactor(1f, 0.5f));
            Check("the factor never drops below 1 while the penalty is off",
                ProductionEvaluator.SecurityFactor(0f, 0f) >= 1f
                && ProductionEvaluator.SecurityFactor(0.5f, 0.1f) >= 1f);

            Section("locality tilts output without dictating it");
            Check("ground no better than the province average is neutral",
                Near(ProductionEvaluator.LocalityFactor(100f, 100f), 1f));
            Check("a caller with no survey loses nothing by passing zeroes",
                Near(ProductionEvaluator.LocalityFactor(0f, 0f), 1f));
            Check("twice-as-rich ground does not double output",
                Near(ProductionEvaluator.LocalityFactor(200f, 100f), 1.5f));
            Check("barren ground is a penalty, not a shutdown",
                Near(ProductionEvaluator.LocalityFactor(0f, 100f), 0.5f));
            Check("absurdly rich ground is still capped",
                Near(ProductionEvaluator.LocalityFactor(99999f, 100f), 1.5f));
            Check("richer ground always beats poorer ground",
                ProductionEvaluator.LocalityFactor(150f, 100f) > ProductionEvaluator.LocalityFactor(80f, 100f));

            Section("composition is bounded");
            Check("neutral everything is neutral",
                Near(ProductionEvaluator.Compose(1f, 1f, 1f, 1f, SettlementTier.None), 1f));
            Check("the best case cannot run away",
                Near(ProductionEvaluator.Compose(2f, 1.5f, 1.25f, 1.5f, SettlementTier.MajorCity), 3f));
            Check("the worst case cannot reach zero",
                Near(ProductionEvaluator.Compose(0.2f, 0.8f, 1f, 0.5f, SettlementTier.None), 0.15f));
            Check("the best province is at most twenty times the worst",
                ProductionRules.MaxTotalFactor / ProductionRules.MinTotalFactor <= 20f);
            Check("an untiered holding is not zeroed by the tier scale",
                ProductionEvaluator.Compose(1f, 1f, 1f, 1f, SettlementTier.None) > 0.9f);
            Check("a bigger settlement produces more, all else equal",
                ProductionEvaluator.Compose(1f, 1f, 1f, 1f, SettlementTier.City)
                    > ProductionEvaluator.Compose(1f, 1f, 1f, 1f, SettlementTier.Village));

            Section("a world with no new data produces exactly what 0.6 produced");
            // 0.6 was: GetResourceScale(stock, baseline) * (0.8 + population / 2000), capped.
            // A caller that has no security survey, no per-tile richness and no tier passes the
            // neutral inputs below, and must land on that number to the digit -- otherwise every
            // existing save's economy shifts the moment 0.7 loads.
            var legacyPool = new ResourcePool(750f);
            float legacyExpected = (750f / 500f) * (0.8f + 900 * (1f / 2000f));
            float legacyActual = ProductionEvaluator.Evaluate(
                legacyPool, ResourceKind.Minerals,
                surroundingPopulation: 900,
                ownershipScore: 0f, security: 0f,
                localRichness: 0f, provinceAverageRichness: 0f,
                tier: SettlementTier.None);
            Check("the composed model reproduces the 0.6 figure exactly", Near(legacyActual, legacyExpected));

            float legacyPoor = ProductionEvaluator.Evaluate(
                new ResourcePool(0f), ResourceKind.Minerals, 0, 0f, 0f, 0f, 0f, SettlementTier.None);
            Check("and reproduces it at the floor too", Near(legacyPoor, 0.2f * 0.8f));

            Section("the model cannot produce a nonsense number");
            float worst = float.MaxValue, best = float.MinValue;
            for (int stock = 0; stock <= 4000; stock += 137)
            {
                for (int pop = 0; pop <= 5000; pop += 419)
                {
                    for (int own = 0; own <= 10; own += 3)
                    {
                        float f = ProductionEvaluator.Evaluate(
                            new ResourcePool(stock), ResourceKind.Minerals, pop,
                            own / 10f, own / 10f, own * 40f, 100f, SettlementTier.City);
                        if (f < worst) worst = f;
                        if (f > best) best = f;
                    }
                }
            }
            Check("every combination stays inside the declared band",
                worst >= ProductionRules.MinTotalFactor - 0.0001f
                && best <= ProductionRules.MaxTotalFactor + 0.0001f);
            Check("and the band is actually exercised, not just respected", best > worst * 2f);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL ECONOMY TESTS PASSED" : failures + " ECONOMY TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool Near(float a, float b, float tolerance = 0.001f)
        {
            return Math.Abs(a - b) < tolerance;
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
