namespace Basil.Domain.Content;

public static class FaqEntryName
{
	public static bool IsValid(string? entry)
	{
		return entry is not null && !entry.Split(':').Any(string.IsNullOrWhiteSpace) &&
		       ContentReference.IsValidPath(FileNameOf(entry));
	}

	public static string FileNameOf(string entry)
	{
		return string.Join('/', entry.Split(':')) + ".txt";
	}
}