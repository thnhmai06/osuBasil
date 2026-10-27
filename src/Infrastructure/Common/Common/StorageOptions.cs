namespace Basil.Infrastructure.Common;

/// <summary>Configures where the server keeps its data.</summary>
public sealed class StorageOptions
{
	/// <summary>Gets or sets the directory that holds the server's stored files.</summary>
	public required string DataDirectory { get; set; }
}