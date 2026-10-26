using System.Collections.Frozen;
using Npgsql;

namespace Basil.Infrastructure.Persistence.Postgres;

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

	extension(Exception exception)
	{
		/// <summary>
		///     Tells whether a write failed because of its environment (connection, server state, configuration), which makes
		///     it worth trying again.
		/// </summary>
		public bool IsEnvironmentException()
		{
			return exception.Database() switch
			{
				PostgresException postgres => postgres.IsTransient || EnvironmentStates.Contains(postgres.SqlState),
				NpgsqlException npgsql => npgsql.IsTransient,
				TimeoutException => true,
				_ => false
			};
		}

		/// <summary>Tells whether the connection to the database failed, so that it is not known whether a commit happened.</summary>
		public bool IsConnectionException()
		{
			return exception.Database() is NpgsqlException and not PostgresException;
		}

		/// <summary>Tells whether a write failed because a value that must be unique is already stored.</summary>
		public bool IsUniqueViolation()
		{
			return exception.Database() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
		}

		/// <summary>
		///     Finds the error the database itself reported inside the exceptions Entity Framework wraps it in, or gives the
		///     exception back when it holds none.
		/// </summary>
		private Exception Database()
		{
			for (var current = exception; current is not null; current = current.InnerException)
				if (current is NpgsqlException or TimeoutException)
					return current;
			return exception;
		}
	}
}