using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class MayChangeFaqHandler(Server server) : IRequestHandler<MayChangeFaq, ServerActionResult>
{
	public ValueTask<ServerActionResult> Handle(MayChangeFaq request, CancellationToken ct)
	{
		return ValueTask.FromResult(server.ScopeTo(request.Actor).Faq.MayChange());
	}
}
