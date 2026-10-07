namespace Basil.Infrastructure.Storage;

/// <summary>Configures where the server keeps its data.</summary>
public sealed class StorageOptions
{
	/// <summary>Gets or sets the directory that holds the server's stored files.</summary>
	public required string DataDirectory { get; set; }

	/// <summary>Gets or sets how to reach the server's PostgreSQL database, as an Npgsql connection string.</summary>
	public required string ConnectionString { get; set; }

	/// <summary>Gets or sets how many write lanes store changes side by side.</summary>
	/// <remarks>
	///     The default is the smallest count that reached near-peak write throughput without slower try-operations
	///     in the measurements of <c>plans/evidence/postgresql-write-lanes-20261008</c>.
	/// </remarks>
	public int WriteLanes { get; set; } = 8;
}