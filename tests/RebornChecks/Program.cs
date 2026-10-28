// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @kaldigo
using System.Text.Json.Nodes;
using Questionable.External.Reborn;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    ++checks;
}
void Reject(Action action, string description)
{
    try { action(); }
    catch (Exception e) when (e is InvalidOperationException or AggregateException) { ++checks; return; }
    throw new InvalidOperationException(description);
}

Dictionary<(string, string), bool> values = new() { [("Zone", "Enabled")] = false, [("Rotation", "Clear")] = true };
List<(string, string, bool)> writes = [];
var session = new RebornSession((n, f) => values[(n, f)], (n, f, v) => { values[(n, f)] = v; writes.Add((n, f, v)); });
session.Override("Zone", "Enabled", true);
session.Override("Zone", "Enabled", true);
Check(writes.Count == 1, "Repeated phases must retain the original setting.");
session.Override("Rotation", "Clear", false);
session.Restore();
Check(!values[("Zone", "Enabled")] && values[("Rotation", "Clear")], "Cleanup restores both original values.");
Check(!session.HasSettings, "Successful cleanup releases ownership.");
session.Restore();
Check(writes.Count == 4, "Repeated cleanup is idempotent.");
session.Override("Zone", "Enabled", false);
Check(!session.HasSettings, "Already-correct settings are not owned.");
session.Override("Zone", "Enabled", true);
values[("Zone", "Enabled")] = false;
Reject(() => session.Override("Zone", "Enabled", true), "A changed setting must stop a later automation phase.");
session.Restore();
Check(!values[("Zone", "Enabled")] && writes.Count == 5, "A subsequent user edit must survive cleanup.");

bool failZoneRestore = true;
var retry = new RebornSession((n, f) => values[(n, f)], (n, f, v) =>
{
    if (n == "Zone" && !v && failZoneRestore) throw new InvalidOperationException("Unavailable transport");
    values[(n, f)] = v;
});
retry.Override("Zone", "Enabled", true);
retry.Override("Rotation", "Clear", false);
Reject(retry.Restore, "Cleanup should report failed restoration.");
Check(values[("Rotation", "Clear")], "One failed restoration must not skip the others.");
Check(retry.HasSettings, "Failed restoration remains retryable.");
failZoneRestore = false;
retry.Restore();
Check(!retry.HasSettings && !values[("Zone", "Enabled")], "A later cleanup retries the failed setting.");

var rejected = new RebornSession((n, f) => false, (n, f, v) => { });
Reject(() => rejected.Override("Zone", "Enabled", true), "A missing write acknowledgement must prevent activation.");
Check(rejected.HasSettings, "Uncertain submissions retain ownership until cleanup.");
rejected.Restore();
Check(!rejected.HasSettings, "An unchanged value releases uncertain ownership.");

foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Presets"), "*.json"))
{
    string original = File.ReadAllText(file);
    string prepared = RebornSession.PreparePreset(original);
    JsonObject parsed = JsonNode.Parse(prepared)!.AsObject();
    Check(parsed["Name"]!.GetValue<string>().EndsWith(" [Reborn]", StringComparison.Ordinal), "Fork presets use distinct names.");
    RebornSession.ValidatePreset(prepared, prepared);
    ++checks;
    JsonObject missingModule = JsonNode.Parse(prepared)!.AsObject();
    missingModule["Modules"]!.AsObject().Remove(parsed["Modules"]!.AsObject().First().Key);
    Reject(() => RebornSession.ValidatePreset(prepared, missingModule.ToJsonString()), "An import that silently drops a module must fail.");
    JsonObject missingSetting = JsonNode.Parse(prepared)!.AsObject();
    var module = missingSetting["Modules"]!.AsObject().First(m => m.Value!.AsArray().Count > 0);
    module.Value!.AsArray().RemoveAt(0);
    Reject(() => RebornSession.ValidatePreset(prepared, missingSetting.ToJsonString()), "An import that silently drops a track/option must fail.");
    Reject(() => RebornSession.ValidatePreset(prepared, null), "Missing preset readback must fail.");
}
Check(RebornSession.IsIntegrationPreset("Questionable [Reborn]"), "Integration preset remains recognisable after a reload.");
Check(!RebornSession.IsIntegrationPreset("User raid preset"), "A user preset must not be cleared by AI shutdown.");
Check(!RebornSession.IsIntegrationPreset(null), "An inactive preset grants no shutdown ownership.");
Console.WriteLine($"Passed {checks} Reborn lifecycle and preset contract checks.");
