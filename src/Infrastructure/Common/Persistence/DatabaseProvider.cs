using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>A pool of connections to the database, created by the provider and released when the server stops.</summary>
public interface IDatabaseSource : IAsyncDisposable;

/// <summary>The engines of the two endpoints: the one that serves reads and the one that stores changes and migrates.</summary>
/// <param name="Read">The engine the read endpoint reaches.</param>
/// <param name="Write">The engine the write endpoint reaches.</param>
public sealed record DatabaseProviders(IDatabaseProvider Read, IDatabaseProvider Write);

/// <summary>The database engine behind the server: how contexts connect to it and how its failures are told apart.</summary>
public interface IDatabaseProvider
{
	/// <summary>Opens a pool of connections.</summary>
	/// <param name="connectionString">How to reach the database.</param>
	/// <param name="poolSize">How many connections the pool holds at most; 0 keeps the engine's default.</param>
	/// <returns>The pool.</returns>
	IDatabaseSource CreateSource(string connectionString, int poolSize);

	/// <summary>Points a context at a pool and at the table in which its module records migrations.</summary>
	/// <param name="builder">The options being built.</param>
	/// <param name="source">The pool the context uses.</param>
	/// <param name="historyTable">The table in which the module records the migrations it applied.</param>
	void Configure(DbContextOptionsBuilder builder, IDatabaseSource source, string historyTable);

	/// <summary>Tells whether a write failed because a value that must be unique is already stored.</summary>
	/// <param name="exception">The failure of the write.</param>
	bool IsUniqueViolation(Exception exception);

	/// <summary>Tells whether a failure means the database could not be reached, so trying again may succeed.</summary>
	/// <param name="exception">The failure of opening the connection.</param>
	bool IsConnectionFailure(Exception exception);
}