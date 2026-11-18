namespace Basil.Infrastructure.Shared.Persistence;

public sealed class DatabaseOptions
{
	public required string WriteConnectionString { get; set; }

	public string? ReadConnectionString { get; set; }

	public string? WriteProvider { get; set; }

	public string? ReadProvider { get; set; }

	public int ReadPoolSize { get; set; }

	public int WritePoolSize { get; set; } = 8;
}