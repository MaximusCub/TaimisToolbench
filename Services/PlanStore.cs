using System;
using System.IO;
using System.Text;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Loads/saves the generated Crafting Plan tab's content so it survives
    /// a module close/reopen. Single-file JSON, atomic .tmp+Replace write,
    /// gzip container - LoadLatest sniffs the first two bytes for the gzip
    /// magic number (0x1F 0x8B), so a plain-JSON plan.json written before
    /// the container changed still loads.
    /// <para>
    /// Contract a caller can violate: a MISSING file is silent (a fresh
    /// start with no plan is the ordinary first-run case). An UNREADABLE
    /// one never is, and its two verdicts carry two severities - a corrupt
    /// or unparseable file goes to onError at Warn, a file stamped below
    /// PersistedPlan.MinimumReadableSchemaVersion to onInfo at Info. A
    /// caller wiring one and not the other silently drops half the story.
    /// Both verdicts cost the RESULT and keep the REQUEST, so LoadLatest returns a
    /// PersistedPlanLoad rather than a plan; null means only "nothing to
    /// restore". Save takes an internal lock because it has two genuinely
    /// independent callers - see the field's own comment.
    /// </para>
    /// <para>Derivation: docs/ARCHITECTURE.md section 12.</para>
    /// </summary>
    internal class PlanStore
    {
        private readonly string _filePath;

        // See StatusStore's
        // matching field comment.
        private readonly Action<string, Exception> _onError;

        // The benign counterpart of _onError, unique to this store: it
        // carries the one load outcome that is neither a failure nor
        // silent. Severity lives at the wiring site (Module.cs), same as
        // _onError - the store itself stays logging-framework-free.
        private readonly Action<string> _onInfo;

        // Serializes Save only - see this class's own doc comment for why
        // (two genuinely independent callers, unlike every other store in
        // this module). LoadLatest needs no lock: the atomic .tmp+Replace
        // write below means a concurrent read can only ever observe the
        // fully-old or fully-new file, never a torn one - the same
        // reasoning that motivated the atomic-write pattern in the first
        // place.
        private readonly object _saveLock = new object();

        public PlanStore(
            string dataDirectoryPath,
            Action<string, Exception> onError = null,
            Action<string> onInfo = null)
        {
            _filePath = Path.Combine(dataDirectoryPath, "plan.json");
            _onError = onError;
            _onInfo = onInfo;
        }

        public PersistedPlanLoad LoadLatest()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(_filePath);
                string json = GzipJsonFile.IsGzip(bytes)
                    ? GzipJsonFile.DecompressToJson(bytes)
                    : Encoding.UTF8.GetString(bytes);
                var load = PlanStoreHelpers.LoadPersistedPlanDocument(json);
                if (load == null || load.HasResult)
                {
                    return load;
                }

                return ReportDiscardedResult(load);
            }
            catch (Exception ex)
            {
                _onError?.Invoke($"Failed to load plan from {_filePath}", ex);
                return null;
            }
        }

        // One log line for a discarded result, on the channel its cause
        // earns - drift to onInfo, damage to onError, exactly as before.
        // What changed is the tail: the outcome is no longer always "fresh
        // start", so the message has to state which of the two it was.
        private PersistedPlanLoad ReportDiscardedResult(PersistedPlanLoad load)
        {
            bool hasRequest = CountsAsRestorableRequest(load.Plan);
            string outcome = hasRequest
                ? " Your requested items are restored - press Generate Plan to price them again."
                : " Nothing else was restorable, so this session starts fresh.";

            if (load.ResultDiscardCause is PlanSchemaVersionMismatchException)
            {
                // Deliberately NOT routed through _onError: nothing failed.
                _onInfo?.Invoke(load.ResultDiscardCause.Message + outcome);
            }
            else
            {
                _onError?.Invoke(
                    $"Could not read the saved result in {_filePath}.{outcome}",
                    load.ResultDiscardCause);
            }

            return hasRequest ? load : null;
        }

        // A request of nothing is not a request: an entry with no usable
        // rows would reseed an empty input strip and replace the tab's own
        // default row with nothing at all.
        private static bool CountsAsRestorableRequest(PersistedPlan plan)
        {
            if (plan?.RequestItems == null)
            {
                return false;
            }

            foreach (var item in plan.RequestItems)
            {
                if (item != null)
                {
                    return true;
                }
            }

            return false;
        }

        // Atomic .tmp+Replace write, matching SnapshotStore/StatusStore/
        // VendorOfferStore's own one-store-convention - a crash/
        // power-loss mid-write can never leave a half-written plan.json
        // that LoadLatest then fails to parse.
        public void Save(PersistedPlan plan)
        {
            try
            {
                lock (_saveLock)
                {
                    string dir = Path.GetDirectoryName(_filePath);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string json = Serialize(plan);
                    byte[] compressed = GzipJsonFile.Compress(json);
                    string tmpPath = _filePath + ".tmp";
                    File.WriteAllBytes(tmpPath, compressed);

                    if (File.Exists(_filePath))
                    {
                        File.Replace(tmpPath, _filePath, null);
                    }
                    else
                    {
                        File.Move(tmpPath, _filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _onError?.Invoke($"Failed to save plan to {_filePath}", ex);
            }
        }

        internal static string Serialize(PersistedPlan plan)
        {
            return PlanStoreHelpers.SerializePersistedPlan(plan);
        }

        internal static PersistedPlan Deserialize(string json)
        {
            return PlanStoreHelpers.DeserializePersistedPlan(json);
        }
    }
}
