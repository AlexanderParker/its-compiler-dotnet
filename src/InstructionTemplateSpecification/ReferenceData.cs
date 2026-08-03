using System.Text.Json;
using System.Text.Json.Nodes;

namespace InstructionTemplateSpecification;

/// <summary>
/// Reference data sections: placeholders name tabular data sources through
/// the reserved dataSource config key (with an optional dataLimit cap), and
/// the compiler renders each referenced variable once above the template.
/// Rendering is byte-compatible with the reference compilers.
/// </summary>
public static class ReferenceData
{
    public const string Instruction =
        "Use the REFERENCE DATA section as context when generating placeholder content"
        + " - never include the reference data itself in your output";

    public sealed record DataSourceRequest(string Name, int? Limit);

    /// <summary>Collects (name, limit) requests, deduplicated in order of first appearance; the most generous request wins.</summary>
    public static List<DataSourceRequest> CollectDataSources(JsonArray content)
    {
        var requests = new List<DataSourceRequest>();
        foreach (var element in content)
        {
            if (element is not JsonObject obj) continue;
            if (obj["type"]?.GetValue<string>() != "placeholder") continue;
            if (obj["config"] is not JsonObject config) continue;

            int? limit = null;
            if (config["dataLimit"] is JsonValue rawLimit && rawLimit.TryGetValue<int>(out var parsed) && parsed >= 1)
            {
                limit = parsed;
            }

            var candidates = config["dataSource"] switch
            {
                JsonValue value when value.TryGetValue<string>(out var single) => new List<string> { single },
                JsonArray list => list.Select(item => item?.GetValue<string>() ?? "").ToList(),
                _ => new List<string>(),
            };

            foreach (var candidate in candidates.Where(name => name.Length > 0))
            {
                var index = requests.FindIndex(request => request.Name == candidate);
                if (index < 0)
                {
                    requests.Add(new DataSourceRequest(candidate, limit));
                }
                else if (requests[index].Limit is int existing)
                {
                    requests[index] = requests[index] with { Limit = limit is int value ? Math.Max(existing, value) : null };
                }
            }
        }
        return requests;
    }

    private static string RenderCell(JsonNode? value)
    {
        var text = value is JsonValue scalar && scalar.TryGetValue<string>(out var raw)
            ? raw
            : value?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
        return text.Replace("|", "\\|").Replace("\n", " ");
    }

    private static string RenderObjectTable(List<JsonObject> rows)
    {
        var columns = new List<string>();
        foreach (var row in rows)
        {
            foreach (var pair in row)
            {
                if (!columns.Contains(pair.Key)) columns.Add(pair.Key);
            }
        }
        var lines = new List<string>
        {
            "| " + string.Join(" | ", columns) + " |",
            "| " + string.Join(" | ", columns.Select(_ => "---")) + " |",
        };
        foreach (var row in rows)
        {
            lines.Add("| " + string.Join(" | ", columns.Select(column =>
                row.TryGetPropertyValue(column, out var cell) ? RenderCell(cell) : "")) + " |");
        }
        return string.Join("\n", lines);
    }

    /// <summary>Renders one data source as markdown, capped at limit items or fields with the truncation stated.</summary>
    public static string RenderDataSource(JsonNode? value, int? limit = null)
    {
        if (value is JsonArray array)
        {
            var items = limit is int cap && cap < array.Count ? array.Take(cap).ToList() : array.ToList();
            var note = items.Count < array.Count ? $"\n\nShowing the first {items.Count} of {array.Count} items." : "";
            if (items.Count > 0 && items.All(item => item is JsonObject))
            {
                return RenderObjectTable(items.Cast<JsonObject>().ToList()) + note;
            }
            return string.Join("\n", items.Select(item => $"- {RenderCell(item)}")) + note;
        }
        if (value is JsonObject obj)
        {
            var entries = obj.ToList();
            var shown = limit is int cap && cap < entries.Count ? entries.Take(cap).ToList() : entries;
            var note = shown.Count < entries.Count ? $"\n\nShowing the first {shown.Count} of {entries.Count} fields." : "";
            var lines = new List<string> { "| Field | Value |", "| --- | --- |" };
            lines.AddRange(shown.Select(pair => $"| {pair.Key.Replace("|", "\\|")} | {RenderCell(pair.Value)} |"));
            return string.Join("\n", lines) + note;
        }
        return RenderCell(value);
    }
}
