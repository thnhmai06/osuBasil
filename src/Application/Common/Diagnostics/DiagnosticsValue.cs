namespace Basil.Application.Common.Diagnostics;

/// <summary>One live count that a module reports about itself, as a named number of a diagnostics category.</summary>
/// <param name="Category">The diagnostics category the count belongs to, such as <c>application</c>.</param>
/// <param name="Name">The name of the count inside the category, such as <c>usersOnline</c>.</param>
/// <param name="Value">The count.</param>
public readonly record struct DiagnosticsValue(string Category, string Name, long Value);
