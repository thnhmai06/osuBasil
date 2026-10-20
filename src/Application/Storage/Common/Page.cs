namespace Basil.Application.Storage.Common;

/// <summary>One part of a listing.</summary>
/// <param name="Items">The items in this part, in the listing's order.</param>
/// <param name="Total">How many items the whole listing has.</param>
/// <typeparam name="T">The type of the listed items.</typeparam>
public sealed record Page<T>(IReadOnlyList<T> Items, int Total);