using System.Text.Json;
using System.Text.Json.Nodes;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Produces RFC 7396 (JSON Merge Patch) documents diffing two JSON object states — used to turn
///     "full snapshot on every change" into "full snapshot once, then a patch per change" for the
///     live SSE channels. Per RFC 7396, a member missing from <c>current</c> that was present in
///     <c>previous</c> is represented as <c>null</c> (meaning "remove this member"); a changed member
///     is replaced wholesale (arrays are never merged element-by-element, only replaced); an
///     unchanged member is omitted from the patch entirely. Only object members are diffed
///     recursively — arrays and scalars are compared by value and, if different, included in full.
/// </summary>
public static class JsonMergePatch
{
    /// <summary>
    ///     Diffs two values of the same type by serializing both to <see cref="JsonNode" /> first.
    ///     Returns <c>null</c> (no patch needed) when nothing changed.
    /// </summary>
    public static JsonNode? Diff<T>(T previous, T current, JsonSerializerOptions? options = null)
    {
        var previousNode = JsonSerializer.SerializeToNode(previous, options);
        var currentNode = JsonSerializer.SerializeToNode(current, options);
        return Diff(previousNode, currentNode);
    }

    /// <summary>Diffs two already-parsed JSON trees. Returns <c>null</c> when nothing changed.</summary>
    public static JsonNode? Diff(JsonNode? previous, JsonNode? current)
    {
        if (previous is JsonObject previousObject && current is JsonObject currentObject)
            return DiffObjects(previousObject, currentObject);

        return JsonNode.DeepEquals(previous, current) ? null : current?.DeepClone();
    }

    private static JsonNode? DiffObjects(JsonObject previous, JsonObject current)
    {
        var patch = new JsonObject();

        foreach (var (key, previousValue) in previous)
            if (!current.ContainsKey(key))
                patch[key] = null;

        foreach (var (key, currentValue) in current)
        {
            if (!previous.TryGetPropertyValue(key, out var previousValue))
            {
                patch[key] = currentValue?.DeepClone();
                continue;
            }

            if (previousValue is JsonObject previousChildObject && currentValue is JsonObject currentChildObject)
            {
                var nested = DiffObjects(previousChildObject, currentChildObject);
                if (nested is JsonObject { Count: > 0 })
                    patch[key] = nested;
                continue;
            }

            if (!JsonNode.DeepEquals(previousValue, currentValue))
                patch[key] = currentValue?.DeepClone();
        }

        return patch;
    }
}
