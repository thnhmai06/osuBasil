namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>One change to store, applied to a context; it works on values copied when the change was made.</summary>
/// <param name="Run">Applies the change to the context.</param>
internal sealed record WriteCommand(Func<BasilDbContext, CancellationToken, Task> Run);