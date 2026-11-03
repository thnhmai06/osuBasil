namespace Basil.Host.Shared.Documentation;

/// <summary>A page of the documentation that the part of the server it describes writes about itself.</summary>
public interface IDocumentPage
{
	/// <summary>Gets the name of the page in its address.</summary>
	string Slug { get; }

	/// <summary>Gets the title of the page.</summary>
	string Title { get; }

	/// <summary>Writes the page in Markdown, as the running server behaves.</summary>
	/// <returns>The Markdown text.</returns>
	string Markdown();
}
