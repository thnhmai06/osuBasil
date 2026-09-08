namespace Basil.Server.Shared.Eventing;

/// <summary>
///     Identifies one broadcast stream on <see cref="LiveEventHub" />, e.g. a match's <c>main</c>
///     state channel or the diagnostic API's own category of live updates.
/// </summary>
/// <param name="Category">The feature-defined family of streams, e.g. <c>"match"</c>.</param>
/// <param name="Id">The identifier of the specific entity within <paramref name="Category" />.</param>
/// <param name="Name">The specific stream within that entity, e.g. <c>"main"</c> or <c>"settings"</c>.</param>
public readonly record struct StreamKey(string Category, int Id, string Name);
