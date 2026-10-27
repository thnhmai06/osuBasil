namespace Basil.Application.Content.Persistence;

/// <summary>Writes the new text of an FAQ entry.</summary>
/// <remarks>
///     The stored entry keeps its previous text until <see cref="Commit" /> is called; a writer disposed without a
///     commit discards its text.
/// </remarks>
public interface IFaqEntryWriter : IAsyncDisposable, IDisposable
{
	/// <summary>Writes text to the entry.</summary>
	/// <param name="text">The text to write.</param>
	/// <returns>A task that ends when the text has been written.</returns>
	Task WriteAsync(string text);

	/// <summary>Stores the text written so far, replacing the entry's previous text; committing again does nothing.</summary>
	void Commit();
}