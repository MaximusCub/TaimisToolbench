# Golden plan fixtures

One serialized plan per shipped `PersistedPlan.CurrentSchemaVersion`, plus
one per shipped `PlanHistoryIndex.CurrentSchemaVersion`. The current build
must still restore the **request** out of every one of them, and the whole
**result** out of every one stamped at or above
`PersistedPlan.MinimumReadableSchemaVersion`. That is the compatibility
contract, stated in full in
[`docs/ARCHITECTURE.md`](../../../docs/ARCHITECTURE.md) section 12; section
12.5 says which past versions are readable and on what evidence.

## Why the files are here

A code fix that no test pins will regress. These are the bytes an older
build actually wrote, so "we did not break anyone's saved plans" is a
thing CI can check rather than a thing a reviewer has to believe.

Two gates read this directory:

- `tests/TaimisToolbench.Tests/Services/PlanCompatibilityFixtureTests.cs`
  loads every fixture through the real `PlanStore`, the real gzip
  container and the real deserializer.
- The **"Saved plans from older builds still load"** step in
  `.github/workflows/tests.yml` checks the corpus is COMPLETE - one
  fixture per shipped version, each stamped with the version its name
  claims and carrying the whole request layer. It needs no build, so a
  missing fixture fails on Linux in seconds.

## Adding one (the whole workflow)

When you bump a schema version, run the suite:

```
dotnet test tests/TaimisToolbench.Tests
```

`CurrentSchemaVersions_HaveAFixture_CapturingOneIfMissing` captures the
missing fixture from the real serializer, writes it here, and fails
telling you to review the diff and `git add` it. Do that in the same
commit as the bump - the file is what proves the NEXT build can still
restore plans written by this one.

Fixtures are only ever added. Deleting one retires a promise to the users
still running that build.

## What is in the corpus, and how each file was made

| File | Provenance |
| --- | --- |
| `plan-v4.json` | Captured live from `PlanStoreHelpers.SerializePersistedPlan` by `CurrentSchemaVersions_HaveAFixture_CapturingOneIfMissing`, on the bump that dropped `VendorOffer.Locations`. |
| `plan-v3.json` | Captured live from `PlanStoreHelpers.SerializePersistedPlan` over a two-item plan solved by the real `CraftingPlanPipeline`. |
| `plan-v2.json` | `plan-v3.json` restamped to schema 2. |
| `plan-v1.json` | `plan-v2.json` restamped to schema 1, minus `ValueOwnMaterials` - the one request-layer member that did not exist at v1 (added by commit `c55596a`, which made the 1 -> 2 bump). |
| `plan-v1-alien-result.json` | `plan-v1.json` with every property name inside `Result` prefixed, so no member of the result graph can bind to anything. It is the hostile case the split exists for: proof that a request survives a result this build cannot read at all. |
| `plan-history-index-v1.json` | Captured live from `PlanHistoryStore.Save`. |

None of the pre-split fixtures carries a `RequestSchemaVersion` field,
because no build that wrote them had one. That is not an omission to
tidy up: it is what makes them exercise the rule that an absent request
version reads as 1 rather than as "unrecorded".

`plan-v1.json` and `plan-v2.json` are restamps rather than captures
because no build that writes those versions still exists to run. Their
`Result` subtrees therefore carry the current shape, which is deliberate
and costs nothing: both sit below the readable floor, so the contract
discards their result **unread**, and `plan-v1-alien-result.json` is the
fixture that proves the result's shape cannot matter. Every fixture from
v3 on is a live capture, and v3 is the oldest one whose result must still
load whole.

The fixtures are checked in uncompressed so a reviewer can read the diff;
the store writes gzip, and `PlanStore.LoadLatest` sniffs the magic number,
so both are the same supported file. The test compresses each fixture
before loading it, exercising the container the module actually writes.
