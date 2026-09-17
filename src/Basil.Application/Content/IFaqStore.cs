namespace Basil.Application.Content;

/// <summary>Reads the stored FAQ entries.</summary>
public interface IFaqStore
{
	/// <summary>
	///     Lists the names of all stored FAQ entries, including ones nested in subdirectories.
	/// </summary>
	/// <returns>
	///     The entry names without their <c>.txt</c> extension, ordered case-insensitively, or an empty list when the
	///     folder does not exist. A nested entry's name joins its directory segments with <c>:</c>.
	/// </returns>
	IReadOnlyList<string> ListEntries();

	/// <summary>Reads a single FAQ entry.</summary>
	/// <param name="entry">The entry name without its <c>.txt</c> extension.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The entry content, or <see langword="null" /> when the entry does not exist.</returns>
	Task<string?> ReadEntryAsync(string entry, CancellationToken cancellationToken = default);
}