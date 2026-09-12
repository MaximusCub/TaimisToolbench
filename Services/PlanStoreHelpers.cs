using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Serialization for plan persistence. Two deliberate differences from
    /// SnapshotHelpers' shape: DeserializePersistedPlan does NOT swallow a
    /// parse/schema failure into a silent null - it lets the exception
    /// propagate to PlanStore.LoadLatest's single try/catch, which owns the
    /// Warn-vs-Info split - and the formatting is compact rather than
    /// Indented (see SerializePersistedPlan's own doc comment).
    /// <para>
    /// Two read entry points, because a plan.json is two independently
    /// versioned layers: LoadPersistedPlanDocument reads both and degrades
    /// to the request alone when the result layer is unreadable;
    /// DeserializePersistedPlan is the strict all-or-nothing read, taken by
    /// PlanHistoryBlobStore, whose index row already carries the request.
    /// </para>
    /// <para>Derivation: docs/ARCHITECTURE.md section 12.</para>
    /// </summary>
    internal static class PlanStoreHelpers
    {
        // Raised from Newtonsoft's default 64: a persisted +24 Agony
        // Infusion plan (23 recipe levels, the deepest chain in the game
        // per docs/research/minimum-window-width.md) nests ~3 JSON levels
        // per tree node and failed to load with the default. Saving is
        // unaffected - Json.NET only enforces MaxDepth on read. 512 covers
        // the validator's 200-domain-level bound at ~3 JSON levels each.
        private const int ReadMaxDepth = 512;

        /// <summary>
        /// The members that reach the RESULT graph, skipped whole (never
        /// bound to a type) by the request-only read - which is exactly
        /// what makes a request survive a result-shape change it could
        /// not possibly deserialize. See docs/ARCHITECTURE.md section 12.
        /// </summary>
        internal static readonly IReadOnlyList<string> ResultGraphMembers = new[]
        {
            nameof(PersistedPlan.Result),
            nameof(PersistedPlan.NodeOverrides),
        };

        /// <summary>
        /// The members a request-only restore reseeds the tab from. Named
        /// rather than left implicit so that
        /// PlanCompatibilityFixtureTests' layer-classification test can
        /// require every PersistedPlan member to sit in exactly one of
        /// these three lists - a member in none of them would be skipped
        /// by the request read and silently default on restore.
        /// </summary>
        internal static readonly IReadOnlyList<string> RequestLayerMembers = new[]
        {
            nameof(PersistedPlan.GeneratedAt),
            nameof(PersistedPlan.RequestItems),
            nameof(PersistedPlan.UseOwnMaterials),
            nameof(PersistedPlan.PriceBasis),
            nameof(PersistedPlan.ValueOwnMaterials),
            nameof(PersistedPlan.IgnoredItemIds),
        };

        /// <summary>The two version stamps, one per layer.</summary>
        internal static readonly IReadOnlyList<string> VersionMembers = new[]
        {
            nameof(PersistedPlan.SchemaVersion),
            nameof(PersistedPlan.RequestSchemaVersion),
        };

        /// <summary>
        /// Serializes a PersistedPlan to a JSON string. Returns null if
        /// plan is null.
        /// <para>
        /// compact
        /// (Formatting.None), NOT Indented like SnapshotHelpers'/
        /// StatusStore's own precedent - a PersistedPlan carries the FULL
        /// SolveContext (the whole reduced crafting tree, every priced
        /// item, every vendor offer), so indentation is not a flat per-file
        /// constant the way it is for snapshot.json; a synthetic
        /// 364-node/400-priced-item tree measured 527 KB indented vs. 216
        /// KB compact. This runs on every override-resolve pill click, not
        /// just once per Generate (see Module.PersistResolvedPlanInBackground),
        /// so the readability Indented buys elsewhere is not worth doubling
        /// the bytes serialized/written on an interactive path.
        /// </para>
        /// </summary>
        internal static string SerializePersistedPlan(PersistedPlan plan)
        {
            if (plan == null)
            {
                return null;
            }

            return JsonConvert.SerializeObject(plan, Formatting.None);
        }

        /// <summary>
        /// The STRICT read: a whole PersistedPlan or nothing. Returns null
        /// for null/whitespace input. Throws (does not swallow) for
        /// malformed JSON, a document too degraded to render safely (no
        /// Result/Plan at all), a SchemaVersion outside the readable range
        /// (as PlanSchemaVersionMismatchException below it, as
        /// InvalidDataException for an unrecorded (0), negative or
        /// newer-than-this-build one - see
        /// PersistedPlan.MinimumReadableSchemaVersion's own doc
        /// comment), or a
        /// structurally-valid-but-degraded object graph (round 4 review-fix,
        /// critical - see PlanStructuralValidator's own doc comment) - see
        /// this class's own doc comment for why.
        /// </summary>
        internal static PersistedPlan DeserializePersistedPlan(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var plan = JsonConvert.DeserializeObject<PersistedPlan>(
                json, new JsonSerializerSettings { MaxDepth = ReadMaxDepth });

            // A structurally valid but too-degraded-to-render object (e.g.
            // a JSON document that happened to parse but was never a real
            // PersistedPlan at all) must not be handed back as if it were
            // usable - "never partially render".
            //
            // Damage and drift are two different verdicts and must never
            // share one message: they were merged until 2026-08-23, when a
            // routine rejection of an 8-day-old file logged as a possible
            // corruption and took a forensic reconstruction to explain.
            // Result/Plan has existed since schema 1, so a document without
            // it was never a valid plan file at ANY version - that is
            // damage, and only damage may say "corrupt". Checked first for
            // exactly that reason.
            if (plan?.Result?.Plan == null)
            {
                throw new InvalidDataException(
                    "Persisted plan is missing Result/Plan - corrupt file.");
            }

            // Drift: a recognizable plan file stamped BELOW the readable
            // range - a shipped version whose shape this build can no
            // longer be sure of. Expected, benign, and self-healing on the
            // next Generate, so it carries its own exception type -
            // PlanStore.LoadLatest reports it at Info, not Warn. Which
            // versions sit inside the range, and on what evidence, is
            // PersistedPlan.MinimumReadableSchemaVersion's own doc comment.
            //
            // Two versions are deliberately NOT drift and take the error
            // channel instead, each with its own wording:
            //   0 - never a shipped version. It is what Newtonsoft leaves
            //       when the field is absent (PersistedPlan.SchemaVersion
            //       carries no initializer precisely so absence stays
            //       detectable), so it means either a file older than the
            //       version gate itself or a construction site that forgot
            //       to stamp it - a module defect whose every save is
            //       silently unrestorable. Info would bury that.
            //   above current or negative - a version this build never
            //       shipped. A newer build wrote it and this one cannot
            //       know what is in it; only a hand-edited file is
            //       negative.
            int observed = plan.SchemaVersion;
            int expected = PersistedPlan.CurrentSchemaVersion;
            int oldest = PersistedPlan.MinimumReadableSchemaVersion;
            if (observed >= 1 && observed < oldest)
            {
                throw new PlanSchemaVersionMismatchException(observed, oldest, expected);
            }

            if (observed < 1 || observed > expected)
            {
                throw new InvalidDataException(observed == 0
                    ? $"Persisted plan records no schema version at all, this build reads {oldest} to {expected} - it predates the version gate, or whatever wrote it never set PersistedPlan.SchemaVersion."
                    : $"Persisted plan is schema {observed}, which this build ({oldest} to {expected}) never shipped - written by a newer build, or the field was damaged.");
            }

            // a single, class-level walk of
            // the ENTIRE restored object graph - the display tree, the
            // solve tree, and every collection the restore-render and local
            // override re-solve paths dereference without a null guard -
            // rather than relying on individual render call sites to each
            // guard themselves (rounds 1-3 proved that approach never
            // converges: every fix revealed exactly one more unguarded
            // site). See PlanStructuralValidator's own doc comment for the
            // full inventory and the exact crash sites this closes.
            if (!PlanStructuralValidator.IsStructurallyValid(plan, out string invalidReason))
            {
                throw new InvalidDataException(
                    $"Persisted plan failed structural validation ({invalidReason}) - corrupt or degraded file.");
            }

            MarkPlanRoots(plan.Result);
            return plan;
        }

        /// <summary>
        /// The TWO-LAYER read, and the only one a restore should use:
        /// PersistedPlanLoad.Full when the whole document was readable,
        /// PersistedPlanLoad.RequestOnly carrying the cause when only the
        /// result layer was not. Returns null for null/whitespace input.
        /// <para>
        /// Throws only when the REQUEST layer itself is unreadable: a
        /// document that is not JSON at all, or one whose
        /// RequestSchemaVersion is above this build's. Everything else -
        /// result-schema drift, a damaged or absent result, a result
        /// written to a shape this build cannot bind - costs the result
        /// and keeps the plan. That asymmetry is the contract; see
        /// docs/ARCHITECTURE.md section 12.
        /// </para>
        /// </summary>
        internal static PersistedPlanLoad LoadPersistedPlanDocument(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            PersistedPlan full = null;
            Exception resultFailure = null;
            try
            {
                full = DeserializePersistedPlan(json);
            }
            catch (Exception ex)
            {
                resultFailure = ex;
            }

            if (resultFailure == null)
            {
                // Checked on the success path too. A future build could in
                // principle break the request layer without touching the
                // result graph, and this build would otherwise reseed the
                // tab from members it had misread.
                RejectNewerRequestLayer(full);
                return PersistedPlanLoad.Full(full);
            }

            var request = DeserializeRequestLayer(json);
            if (request == null)
            {
                throw new InvalidDataException(
                    "Saved plan has no readable request layer - corrupt file.");
            }

            RejectNewerRequestLayer(request);
            return PersistedPlanLoad.RequestOnly(request, resultFailure);
        }

        /// <summary>
        /// Binds the request layer alone, with every member in
        /// <see cref="ResultGraphMembers"/> skipped as an unread token
        /// rather than deserialized into a type. Same document, same
        /// property names, same file - there is no second on-disk shape,
        /// and no tolerant read of the result graph either: it is not read
        /// at all.
        /// </summary>
        internal static PersistedPlan DeserializeRequestLayer(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<PersistedPlan>(
                json,
                new JsonSerializerSettings
                {
                    MaxDepth = ReadMaxDepth,
                    ContractResolver = RequestLayerResolver.Instance,
                });
        }

        private static void RejectNewerRequestLayer(PersistedPlan plan)
        {
            int observed = plan.RequestSchemaVersion;
            int max = PersistedPlan.CurrentRequestSchemaVersion;
            if (observed > max)
            {
                throw new InvalidDataException(
                    $"Saved plan's request is schema {observed}, and this build reads at most {max} - it was written by a newer build.");
            }
        }

        // The resolver behind DeserializeRequestLayer. Scoped to
        // PersistedPlan's own declarations so a same-named member on
        // another type in the graph is unaffected.
        private sealed class RequestLayerResolver : DefaultContractResolver
        {
            internal static readonly RequestLayerResolver Instance = new RequestLayerResolver();

            private static readonly HashSet<string> Skipped =
                new HashSet<string>(ResultGraphMembers, StringComparer.Ordinal);

            protected override JsonProperty CreateProperty(
                MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                if (member.DeclaringType == typeof(PersistedPlan)
                    && Skipped.Contains(property.PropertyName))
                {
                    property.Ignored = true;
                }

                return property;
            }
        }

        /// <summary>
        /// Re-derives CraftingTreeNode.IsPlanRoot, which is internal and
        /// therefore never serialized (see the member's own comment for
        /// why it is kept off the persisted graph). Restore is the one path
        /// that produces a CraftingPlanResult without going through
        /// CraftingTreeBuilder.BuildTree, so without this a restored plan's
        /// root rows would offer the IGNORE pill the flag exists to
        /// suppress until the next Generate or override re-solve. The roots
        /// are exactly what BuildTree was called with: CraftingTree for a
        /// single-item plan, every MultiItemRoots entry for a batch.
        /// </summary>
        private static void MarkPlanRoots(CraftingPlanResult result)
        {
            if (result.CraftingTree != null)
            {
                result.CraftingTree.IsPlanRoot = true;
            }

            if (result.MultiItemRoots == null)
            {
                return;
            }

            foreach (var root in result.MultiItemRoots)
            {
                if (root != null)
                {
                    root.IsPlanRoot = true;
                }
            }
        }
    }

    /// <summary>
    /// A recognizable plan file written by a build at a shipped
    /// PersistedPlan.SchemaVersion BELOW
    /// PersistedPlan.MinimumReadableSchemaVersion (an unrecorded, negative
    /// or newer version is not this, and does not come here - see the throw
    /// site's own comment in DeserializePersistedPlan). Kept distinct from the plain
    /// InvalidDataException the corrupt/degraded paths throw so the ONE
    /// caller that can tell a user anything - PlanStore.LoadLatest - can
    /// report it as the routine, self-healing event it is (Info, no
    /// "corrupt") rather than as damage. The message names the observed
    /// version and the whole readable range, which is what the 2026-08-23
    /// incident had to reconstruct from commit timestamps, and it states no
    /// outcome: what survives the rejection is the caller's to know
    /// (PlanStore.ReportDiscardedResult appends it). Derives straight from
    /// Exception only because InvalidDataException is sealed on .NET
    /// Framework; every other handler on this path is a catch-all, so it
    /// still degrades to the same null + one log line.
    /// </summary>
    internal sealed class PlanSchemaVersionMismatchException : Exception
    {
        internal PlanSchemaVersionMismatchException(
            int observedVersion, int oldestReadableVersion, int currentVersion)
            : base($"Saved plan file is schema {observedVersion}, this build reads {oldestReadableVersion} to {currentVersion}.")
        {
        }
    }
}
