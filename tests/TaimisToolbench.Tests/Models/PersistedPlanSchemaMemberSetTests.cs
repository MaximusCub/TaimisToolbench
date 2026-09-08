using TaimisToolbench.Models;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Models
{
    // B1 (quality-phase1-bugs, quality-audit follow-up): the original guard
    // only snapshotted 4 hand-picked types (PersistedPlan/CraftingPlanResult/
    // PlanSolveContext/CraftingTreeNode) and missed every other type reachable
    // through them - RequiredRecipe, VendorOffer, PillSourceCostBreakdown, and
    // several more all grew public properties after the 1 -> 2 bump without
    // this test ever noticing. ModelGraphSignatures walks the full object
    // graph instead, so a rename, addition, removal OR retype anywhere in the
    // persisted graph fails this test. See KNOWN-ISSUES #53.
    //
    // The signature list itself lives in tests/shared/persisted_plan_schema.txt
    // rather than in a C# array literal, so a shape change shows up as a
    // readable text diff; UPDATE_SNAPSHOTS=1 regenerates it. Its SHA-256 is
    // stored on PersistedPlan next to CurrentSchemaVersion, which is what
    // couples the two - see PersistedPlan.SchemaShapeHash.
    public class PersistedPlanSchemaMemberSetTests
    {
        private const string SnapshotRelativePath = "tests/shared/persisted_plan_schema.txt";

        [Fact]
        public void CurrentSchemaVersion_MatchesExpectedValue()
        {
            Assert.Equal(4, PersistedPlan.CurrentSchemaVersion);
        }

        [Fact]
        public void PersistedPlanGraph_PublicMemberSignature_MatchesSnapshot()
        {
            string[] actual = ModelGraphSignatures.For(typeof(PersistedPlan));
            string[] expected = ModelGraphSignatures.ReadSnapshot(SnapshotRelativePath);

            if (ModelGraphSignatures.ShouldUpdateSnapshots())
            {
                ModelGraphSignatures.WriteSnapshot(SnapshotRelativePath, actual);
                expected = actual;
            }

            // Line-by-line text, so the reviewer's diff names the property
            // that moved instead of "collections differ at index 137".
            Assert.Equal(string.Join("\n", expected), string.Join("\n", actual));
        }

        [Fact]
        public void PersistedPlanGraph_ShapeHash_MatchesTheOneStoredBesideTheVersion()
        {
            // The coupling the pair was always described as having and did
            // not: CurrentSchemaVersion_MatchesExpectedValue and the
            // snapshot test are independent, so editing the snapshot alone
            // used to make a shape change green with the version untouched.
            // The hash lives on PersistedPlan, one line from
            // CurrentSchemaVersion, so a graph change cannot be absorbed
            // without editing the version's own neighbourhood.
            string actualHash = ModelGraphSignatures.Sha256(
                string.Join("\n", ModelGraphSignatures.For(typeof(PersistedPlan))));

            Assert.True(
                actualHash == PersistedPlan.SchemaShapeHash,
                "The persisted plan graph's shape changed.\n"
                + "  expected (PersistedPlan.SchemaShapeHash): " + PersistedPlan.SchemaShapeHash + "\n"
                + "  actual:                                   " + actualHash + "\n"
                + "Run the suite with UPDATE_SNAPSHOTS=1 to rewrite "
                + SnapshotRelativePath + ", review that text diff, then set "
                + "SchemaShapeHash to the actual value above and decide "
                + "whether PersistedPlan.CurrentSchemaVersion must be bumped "
                + "(it must, for any rename, removal or retype).");
        }
    }
}
