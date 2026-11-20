namespace Basil.Application.Shared.Paging;

public sealed record Page<T>(IReadOnlyList<T> Items, bool HasNext);