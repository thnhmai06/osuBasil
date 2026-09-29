using Basil.Application.Common.Queries;
namespace Basil.Application.Chat;

/// <summary>Filters stored FAQ entry names by a free-text match.</summary>
/// <param name="Query">The free-text query, or <see langword="null" /> to match every entry.</param>
public sealed record FaqQuery(string? Query = null) : Query<string>;