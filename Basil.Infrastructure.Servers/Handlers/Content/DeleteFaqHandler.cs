using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class DeleteFaqHandler(Server server) : IRequestHandler<DeleteFaq, ServerActionResult>
{
	public async ValueTask<ServerActionResult> Handle(DeleteFaq request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Faq.DeleteAsync(request.Entry);
	}
}