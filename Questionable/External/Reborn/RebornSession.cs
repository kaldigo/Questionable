// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @kaldigo
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Questionable.External.Reborn;

/// <summary>Owns only the settings changed by this integration; preserves subsequent user edits.</summary>
internal sealed class RebornSession(Func<string, string, bool> read, Action<string, string, bool> write)
{
    private readonly Dictionary<(string Node, string Field), (bool Original, bool Applied)> _settings = [];

    public bool HasSettings => _settings.Count != 0;

    public void Override(string node, string field, bool value)
    {
        var key = (node, field);
        bool current = read(node, field);
        if (_settings.TryGetValue(key, out var owned))
        {
            // Repeated solo-duty phases must not replace the original value or override a user edit.
            if (current != owned.Applied)
                throw new InvalidOperationException($"Reborn {node}.{field} was changed during automation; stop before resuming.");
            return;
        }
        if (current == value)
            return;

        // Record ownership before submission: even a failed acknowledgement may have changed the setting.
        _settings.Add(key, (current, value));
        write(node, field, value);
        if (read(node, field) != value)
            throw new InvalidOperationException($"Reborn did not apply {node}.{field}.");
    }

    public void Restore()
    {
        List<Exception> failures = [];
        foreach (var (key, values) in _settings.ToArray())
        {
            try
            {
                if (read(key.Node, key.Field) == values.Applied)
                {
                    write(key.Node, key.Field, values.Original);
                    if (read(key.Node, key.Field) != values.Original)
                        throw new InvalidOperationException($"Reborn did not restore {key.Node}.{key.Field}.");
                }
                _settings.Remove(key);
            }
            catch (Exception e)
            {
                failures.Add(e);
            }
        }
        if (failures.Count != 0)
            throw new AggregateException("Some Reborn settings could not be restored; cleanup can be retried.", failures);
    }

    public static string PresetName(string upstreamName) => upstreamName + " [Reborn]";

    public static string PreparePreset(string upstreamJson)
    {
        JsonObject preset = JsonNode.Parse(upstreamJson)?.AsObject()
            ?? throw new InvalidOperationException("Invalid Questionable preset.");
        preset["Name"] = PresetName(preset["Name"]!.GetValue<string>());
        return preset.ToJsonString();
    }

    /// <summary>Reborn can report import success while skipping unknown modules/tracks/options.</summary>
    public static void ValidatePreset(string requestedJson, string? actualJson)
    {
        JsonObject requested = JsonNode.Parse(requestedJson)!.AsObject();
        JsonObject actual = JsonNode.Parse(actualJson ?? "null")?.AsObject()
            ?? throw new InvalidOperationException("Reborn did not return the imported preset.");
        if (!JsonNode.DeepEquals(requested["Name"], actual["Name"]))
            throw new InvalidOperationException("Reborn returned a different preset.");
        foreach (var (module, settings) in requested["Modules"]!.AsObject())
        {
            if (actual["Modules"]?[module] is not JsonArray imported)
                throw new InvalidOperationException($"Reborn does not support preset module {module}.");
            foreach (JsonNode? setting in settings!.AsArray())
            {
                if (!imported.Any(candidate => setting!.AsObject().All(pair =>
                        JsonNode.DeepEquals(pair.Value, candidate?[pair.Key]))))
                    throw new InvalidOperationException($"Reborn did not retain {module}: {setting}.");
            }
        }
    }
}
