namespace Basil.Infrastructure.Common.Persistence;

/// <summary>Configures how the server reaches its PostgreSQL database and how it stores changes there.</summary>
public sealed class DatabaseOptions
{
	/// <summary>Gets or sets how to reach the database for storing changes, as an Npgsql connection string.</summary>
	public required string WriteConnectionString { get; set; }

	/// <summary>
	///     Gets or sets how to reach the database for reading, as an Npgsql connection string; the write connection
	///     string is used when there is none.
	/// </summary>
	public string? ReadConnectionString { get; set; }

	/// <summary>Gets or sets the name of the database engine the write connection reaches; PostgreSQL when unset.</summary>
	public string? WriteProvider { get; set; }

	/// <summary>Gets or sets the name of the database engine the read connection reaches; the write engine when unset.</summary>
	public string? ReadProvider { get; set; }

	/// <summary>Gets or sets how many connections the reading pool holds at most; 0 keeps the driver's default.</summary>
	public int ReadPoolSize { get; set; }

	/// <summary>Gets or sets how many connections may store changes at the same time; at least 1, 8 by default.</summary>
	public int WritePoolSize { get; set; } = 8;
}