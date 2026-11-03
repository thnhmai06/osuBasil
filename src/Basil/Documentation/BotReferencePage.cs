using Basil.Bot;
using Basil.Host.Shared.Documentation;

namespace Basil.Documentation;

/// <summary>The page about BasilBot's chat commands.</summary>
internal sealed class BotReferencePage : IDocumentPage
{
	/// <inheritdoc />
	public string Slug => "bot";

	/// <inheritdoc />
	public string Title => "BasilBot commands";

	/// <inheritdoc />
	public string Markdown()
	{
		return BotReference.Markdown();
	}
}