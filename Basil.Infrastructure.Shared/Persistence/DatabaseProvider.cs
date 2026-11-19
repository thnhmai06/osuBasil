using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

public interface IDatabaseSource : IAsyncDisposable
{
	ConnectionStats? Connections { get; }
}

public readonly record struct ConnectionStats(int InUse, int Idle);

public sealed record DatabaseProviders(IDatabaseProvider Read, IDatabaseProvider Write);

public interface IDatabaseProvider
{
	IDatabaseSource CreateSource(string connectionString, int poolSize);

	void Configure(DbContextOptionsBuilder builder, IDatabaseSource source, string historyTable);

	bool IsUniqueViolation(Exception exception);

	bool IsConnectionFailure(Exception exception);
}