namespace Basil.Application.Models.Queries;

public abstract record Query<TFor> where TFor : notnull;