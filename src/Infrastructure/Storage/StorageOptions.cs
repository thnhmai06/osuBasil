namespace Basil.Infrastructure.Storage;

/// <summary>Configures where the server keeps its data.</summary>
public sealed class StorageOptions
{
	/// <summary>Gets or sets the directory that holds the server's stored files.</summary>
	public required string DataDirectory { get; set; }

	/// <summary>Gets or sets how to reach the server's PostgreSQL database, as an Npgsql connection string.</summary>
	public required string ConnectionString { get; set; }

	/// <summary>Gets or sets how many write lanes store changes side by side.</summary>
	public int WriteLanes { get; set; } = 4;
}
