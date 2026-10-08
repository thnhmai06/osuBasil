namespace Basil.Infrastructure.Storage;

/// <summary>Configures how the server reaches its PostgreSQL database and how it stores changes there.</summary>
public sealed class DatabaseOptions
{
	/// <summary>Gets or sets how to reach the database, as an Npgsql connection string.</summary>
	public required string ConnectionString { get; set; }

	/// <summary>Gets or sets how many write lanes store changes side by side; at least 1, 8 by default.</summary>
	public int WriteLanes { get; set; } = 8;
}
