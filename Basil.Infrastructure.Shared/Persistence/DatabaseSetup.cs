using Microsoft.Extensions.Configuration;

namespace Basil.Infrastructure.Shared.Persistence;

/// <summary>A required deployment value is missing; the message names the setting to provide.</summary>
public sealed class MissingConfigurationException(string message) : Exception(message);

public static class DatabaseSetup
{
	public static DatabaseOptions Read(IConfiguration configuration)
	{
		var settings = configuration.GetSection("Basil:Database").Get<DatabaseSettings>() ?? new DatabaseSettings();
		var write = configuration.GetConnectionString("Write");
		if (string.IsNullOrWhiteSpace(write))
			throw new MissingConfigurationException(
				"The database is not configured: set the environment variable ConnectionStrings__Write to the Npgsql connection string.");

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
		public Endpoint Read { get; set; } = new();
		public Endpoint Write { get; set; } = new() { PoolSize = 8 };
	}

	private sealed class Endpoint
	{
		public int PoolSize { get; set; }
	}
}