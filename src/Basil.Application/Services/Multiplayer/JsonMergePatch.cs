using System.Text.Json;
using System.Text.Json.Nodes;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Produces RFC 7396 (JSON Merge Patch) documents that diff two JSON object states.
/// </summary>
/// <remarks>
///     Used to turn "full snapshot on every change" into "full snapshot once, then a patch per
///     change" for the live SSE channels. Per RFC 7396, a member missing from <c>current</c> that
///     was present in <c>previous</c> is represented as <c>null</c> (meaning "remove this member");
///     a changed member is replaced wholesale, since arrays are never merged element by element, only
///     replaced; an unchanged member is omitted from the patch entirely. Only object members are
///     divided recursively, while arrays and scalars are compared by value and, if different,
///     included in full.
/// </remarks>
public static class JsonMergePatch
{
	/// <summary>Diffs two values of the same type by serializing both to <see cref="JsonNode" /> first.</summary>
	/// <typeparam name="T">The type of the two values to compare.</typeparam>
	/// <param name="previous">The earlier state to diff from.</param>
	/// <param name="current">The later state to diff to.</param>
	/// <param name="options">The serializer options used for both serializations, or <see langword="null" /> for the defaults.</param>
	/// <returns>The JSON Merge Patch document, or <see langword="null" /> when nothing changed.</returns>
	public static JsonNode? Diff<T>(T previous, T current, JsonSerializerOptions? options = null)
	{
		var previousNode = JsonSerializer.SerializeToNode(previous, options);
		var currentNode = JsonSerializer.SerializeToNode(current, options);
		return Diff(previousNode, currentNode);
	}

	/// <summary>Diffs two already-parsed JSON trees.</summary>
	/// <param name="previous">The earlier tree to diff from.</param>
	/// <param name="current">The later tree to diff to.</param>
	/// <returns>The JSON Merge Patch document, or <see langword="null" /> when nothing changed.</returns>
	public static JsonNode? Diff(JsonNode? previous, JsonNode? current)
	{
		if (previous is JsonObject previousObject && current is JsonObject currentObject)
			return DiffObjects(previousObject, currentObject);

		return JsonNode.DeepEquals(previous, current) ? null : current?.DeepClone();
	}

	private static JsonObject DiffObjects(JsonObject previous, JsonObject current)
	{
		var patch = new JsonObject();
		foreach (var (key, _) in previous)
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
				if (nested is { Count: > 0 })
					patch[key] = nested;
				continue;
			}

			if (!JsonNode.DeepEquals(previousValue, currentValue))
				patch[key] = currentValue?.DeepClone();
		}

		return patch;
	}
}