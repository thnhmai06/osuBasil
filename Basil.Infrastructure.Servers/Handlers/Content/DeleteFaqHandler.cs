using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class DeleteFaqHandler(Server server) : IRequestHandler<DeleteFaq, ServerOutcome>
{
	public async ValueTask<ServerOutcome> Handle(DeleteFaq request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Faq.DeleteAsync(request.Entry);
	}
}