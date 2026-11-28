namespace Basil.Host.Shared.Documentation;

public interface IDocumentPage
{
	string Slug { get; }

	string Title { get; }

	string Markdown();
}