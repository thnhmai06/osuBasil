namespace Basil.Domain.Events;

/// <summary>
///     Marks a type as a domain event: a record of something business-meaningful that has already
///     happened to an aggregate.
/// </summary>
public abstract record Event;