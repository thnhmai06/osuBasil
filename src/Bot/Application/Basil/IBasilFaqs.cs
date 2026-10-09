namespace Basil.Bot.Application.Basil;

/// <summary>Reads the server's FAQ entries.</summary>
public interface IBasilFaqs
{
	/// <summary>Lists the names of the entries.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The entry names.</returns>
	Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Reads the text of an entry.</summary>
	/// <param name="entry">The entry name.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The text, or <see langword="null" /> when no entry has that name.</returns>
	Task<string?> ReadAsync(string entry, CancellationToken cancellationToken = default);
}
