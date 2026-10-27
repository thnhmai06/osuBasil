namespace Basil.Application.Content.Persistence;

/// <summary>Stores the FAQ entries the bot answers with.</summary>
/// <remarks>Entry names may be nested with <c>:</c> (<c>a:b:c</c>).</remarks>
public interface IFaqStorage
{
	/// <summary>Lists the names of the stored entries.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The names of the stored entries.</returns>
	Task<IEnumerable<string>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Opens a writer that replaces an entry's text.</summary>
	/// <param name="entry">The name of the entry.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A writer to write the new text; see <see cref="IFaqEntryWriter" /> for the commits.</returns>
	Task<IFaqEntryWriter> CreateOrUpdateAsync(string entry, CancellationToken cancellationToken = default);

	/// <summary>Opens the text of an entry.</summary>
	/// <param name="entry">The name of the entry to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The text reader, or <see langword="null" /> when no entry has that name.</returns>
	Task<StreamReader?> ReadAsync(string entry, CancellationToken cancellationToken = default);

	/// <summary>Deletes an entry; deleting a missing one does nothing.</summary>
	/// <param name="entry">The name of the entry to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(string entry, CancellationToken cancellationToken = default);
}