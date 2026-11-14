namespace Basil.Domain.Content;

/// <summary>The naming rule of FAQ entries: <c>:</c> separates the folders and the entry.</summary>
public static class FaqEntryName
{
	/// <summary>Tells whether a text can be the name of a FAQ entry.</summary>
	/// <param name="entry">The text to check.</param>
	/// <returns>
	///     <see langword="true" /> when every part between <c>:</c> names something and the entry's file name is a
	///     valid stored file name; otherwise, <see langword="false" />.
	/// </returns>
	public static bool IsValid(string? entry)
	{
		return entry is not null && !entry.Split(':').Any(string.IsNullOrWhiteSpace) &&
		       ContentReference.IsValidPath(FileNameOf(entry));
	}

	/// <summary>Gets the name of the file that holds an entry: its <c>:</c> segments name the folders.</summary>
	/// <param name="entry">The entry name.</param>
	/// <returns>The relative file name of the entry.</returns>
	/// <remarks>The result is a valid stored file name only when <see cref="IsValid" /> accepts <paramref name="entry" />.</remarks>
	public static string FileNameOf(string entry)
	{
		return string.Join('/', entry.Split(':')) + ".txt";
	}
}