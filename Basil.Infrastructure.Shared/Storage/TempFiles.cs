namespace Basil.Infrastructure.Shared.Storage;

public static class TempFiles
{
	public static string Name(string path)
	{
		return Path.Combine(Path.GetDirectoryName(path)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
	}
}