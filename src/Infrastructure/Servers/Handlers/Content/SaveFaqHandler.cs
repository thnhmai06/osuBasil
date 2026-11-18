using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

internal sealed class SaveFaqHandler(Server server) : IRequestHandler<SaveFaq, ServerOutcome>
{
	public async ValueTask<ServerOutcome> Handle(SaveFaq request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Faq.SaveAsync(request.Entry, request.Text);
	}
}