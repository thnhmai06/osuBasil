using Npgsql;

namespace Basil.Infrastructure.Common.Persistence;

/// <summary>Recognises the failures a database write reports that callers act on.</summary>
public static class WriteErrors
{
	extension(Exception exception)
	{
		/// <summary>Tells whether a write failed because a value that must be unique is already stored.</summary>
		public bool IsUniqueViolation()
		{
			return exception.Database() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
		}

		/// <summary>
		///     Finds the error the database itself reported inside the exceptions that wrap it, or gives the exception
		///     back when it holds none.
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