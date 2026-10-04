namespace Basil.Application.Storage.Content;

/// <summary>Stores the FAQ entries the bot answers with.</summary>
/// <remarks>Entry names may be nested with <c>:</c> (<c>a:b:c</c>).</remarks>
public interface IFaqStorage
{
	/// <summary>Lists the names of the stored entries.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The names of the stored entries.</returns>
	Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores an entry's text, replacing any text stored under that name.</summary>
	/// <param name="entry">The name of the entry.</param>
	/// <param name="content">The text bytes.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SaveAsync(string entry, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Opens the text of an entry.</summary>
	/// <param name="entry">The name of the entry to open.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The text bytes, or <see langword="null" /> when no entry has that name.</returns>
	Task<Stream?> OpenAsync(string entry, CancellationToken cancellationToken = default);

	/// <summary>Deletes an entry; deleting a missing one does nothing.</summary>
	/// <param name="entry">The name of the entry to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(string entry, CancellationToken cancellationToken = default);
}