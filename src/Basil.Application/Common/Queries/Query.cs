namespace Basil.Application.Common.Queries;

public abstract record Query<TValue> where TValue : notnull;