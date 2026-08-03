using Basil.Application.Configurations;

namespace Basil.Infrastructure.Persistence;

/// <summary>Builds the SQLite connection string for the persistence layer from <see cref="DatabaseOptions" />.</summary>
/// <remarks>
///     Shared by every caller that opens an SQLite connection, so a repository and the migration
///     runner get the identical connection string for the same configured database file.
/// </remarks>
public static class DatabaseConnectionStringBuilder
{
	/// <param name="options">The database options carrying the path to resolve.</param>
	extension(DatabaseOptions options)
	{
		/// <summary>Builds the SQLite connection string for the database file described by the given options.</summary>
		/// <returns>The connection string.</returns>
		/// <remarks>
		///     The connection string always carries <c>Foreign Keys=True</c>, because SQLite disables
		///     foreign key enforcement per-connection by default while the schema declares foreign keys,
		///     and <c>Default Timeout=5</c>, which maps to SQLite's busy_timeout. The server is
		///     deliberately multithreaded, so concurrent writers across different matches are expected;
		///     without the timeout they would throw SQLITE_BUSY immediately instead of waiting.
		/// </remarks>
		public string Build()
		{
			return $"Data Source={options.ResolvePath()};Foreign Keys=True;Default Timeout=5";
		}

		/// <summary>
		///     Resolves <see cref="DatabaseOptions.Path" /> to an absolute path, anchored to the
		///     executable's directory (not the process's working directory) when relative.
		/// </summary>
		/// <returns>The resolved absolute path.</returns>
		public string ResolvePath()
		{
			return Path.IsPathRooted(options.Path)
				? options.Path
				: Path.Combine(AppContext.BaseDirectory, options.Path);
		}
	}
}