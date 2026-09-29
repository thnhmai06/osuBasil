namespace Basil.Application.Common.Queries;

public abstract record Query<TFor> where TFor : notnull;