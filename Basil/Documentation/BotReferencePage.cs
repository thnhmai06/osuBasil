using Basil.Application.Bot;
using Basil.Host.Shared.Documentation;

namespace Basil.Documentation;

internal sealed class BotReferencePage : IDocumentPage
{
	public string Slug => "bot";

	public string Title => "BasilBot commands";

	public string Markdown()
	{
		return BotReference.Markdown();
	}
}