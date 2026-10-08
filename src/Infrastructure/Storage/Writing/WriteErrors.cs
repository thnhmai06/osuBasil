using System.Collections.Frozen;
using Npgsql;

namespace Basil.Infrastructure.Storage.Writing;

/// <summary>Tells apart the write failures caused by the database's environment from those caused by the write itself.</summary>
internal static class WriteErrors
{
	private static readonly FrozenSet<string> EnvironmentStates = new[]
	{
		// Read-only after a failover.
		"25006",
		// Cancelled or timed out.
		"57014",
		// Authentication errors.
		"28000",
		"28P01",
		// Database missing.
		"3D000",
		// Insufficient privilege.
		"42501"
	}.ToFrozenSet();

	/// <summary>Tells whether a write failed because of its environment (connection, server state, configuration), which makes it worth trying again.</summary>
	public static bool IsEnvironment(Exception exception) => exception switch
	{
		PostgresException postgres => postgres.IsTransient || EnvironmentStates.Contains(postgres.SqlState),
		NpgsqlException npgsql => npgsql.IsTransient,
		TimeoutException => true,
		_ => false
	};
}
