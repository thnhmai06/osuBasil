using Microsoft.Extensions.Configuration;

namespace Basil.Infrastructure.Shared.Persistence;

/// <summary>Reads where the server stores its data (PostgreSQL) from <c>ConnectionStrings</c> and <c>Basil:Database</c>.</summary>
public static class DatabaseSetup
{
	/// <summary>Reads the database settings.</summary>
	/// <param name="configuration">The configuration.</param>
	/// <returns>The settings of the connections that read and the connections that store changes.</returns>
	/// <exception cref="InvalidOperationException">There is no connection string to store changes.</exception>
	public static DatabaseOptions Read(IConfiguration configuration)
	{
		var settings = configuration.GetSection("Basil:Database").Get<DatabaseSettings>() ?? new DatabaseSettings();
		var write = configuration.GetConnectionString("Write");
		if (string.IsNullOrWhiteSpace(write))
			throw new InvalidOperationException("ConnectionStrings:Write is not set.");

		return new DatabaseOptions
		{
			WriteConnectionString = write,
			ReadConnectionString = configuration.GetConnectionString("Read"),
			ReadProvider = string.IsNullOrWhiteSpace(settings.Read.Provider) ? null : settings.Read.Provider,
			WriteProvider = string.IsNullOrWhiteSpace(settings.Write.Provider) ? null : settings.Write.Provider,
			ReadPoolSize = settings.Read.PoolSize,
			WritePoolSize = settings.Write.PoolSize
		};
	}

	private sealed class DatabaseSettings
	{
		public Endpoint Read { get; set; } = new();
		public Endpoint Write { get; set; } = new() { PoolSize = 8 };
	}

	private sealed class Endpoint
	{
		public int PoolSize { get; set; }
		public string? Provider { get; set; }
	}
}