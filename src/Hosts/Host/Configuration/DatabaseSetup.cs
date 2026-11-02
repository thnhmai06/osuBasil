using Basil.Infrastructure.Common.Persistence;

namespace Basil.Host.Configuration;

/// <summary>Reads where the server stores its data from <c>ConnectionStrings</c> and <c>Basil:Database</c>.</summary>
public static class DatabaseSetup
{
	/// <summary>The only database the server can use.</summary>
	public const string SupportedProvider = "PostgreSQL";

	/// <summary>Reads the database settings, and refuses the ones the server cannot honour instead of ignoring them.</summary>
	/// <param name="configuration">The configuration.</param>
	/// <returns>The settings of the connections that read and the connections that store changes.</returns>
	/// <exception cref="InvalidOperationException">
	///     The database is switched off, a provider other than PostgreSQL is chosen, or there is no connection
	///     string to store changes.
	/// </exception>
	public static DatabaseOptions Read(IConfiguration configuration)
	{
		var settings = configuration.GetSection("Basil:Database").Get<DatabaseSettings>() ?? new DatabaseSettings();
		if (!settings.Enabled)
			throw new InvalidOperationException(
				"Basil:Database:Enabled is false, but running without a database is not supported yet.");
		foreach (var (key, provider) in new[] { ("Read", settings.Read.Provider), ("Write", settings.Write.Provider) })
			if (!string.Equals(provider, SupportedProvider, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					$"Basil:Database:{key}:Provider is \"{provider}\", but only {SupportedProvider} is supported.");

		var write = configuration.GetConnectionString("Write");
		if (string.IsNullOrWhiteSpace(write))
			throw new InvalidOperationException("ConnectionStrings:Write is not set.");

		return new DatabaseOptions
		{
			WriteConnectionString = write,
			ReadConnectionString = configuration.GetConnectionString("Read"),
			ReadPoolSize = settings.Read.PoolSize,
			WritePoolSize = settings.Write.PoolSize
		};
	}

	private sealed class DatabaseSettings
	{
		public bool Enabled { get; set; } = true;
		public Endpoint Read { get; set; } = new();
		public Endpoint Write { get; set; } = new() { PoolSize = 8 };
	}

	private sealed class Endpoint
	{
		public string Provider { get; set; } = SupportedProvider;
		public int PoolSize { get; set; }
	}
}
