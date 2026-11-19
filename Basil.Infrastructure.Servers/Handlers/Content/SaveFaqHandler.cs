using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class SaveFaqHandler(Server server) : IRequestHandler<SaveFaq, ServerActionResult>
{
	public async ValueTask<ServerActionResult> Handle(SaveFaq request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Faq.SaveAsync(request.Entry, request.Text);
	}
}