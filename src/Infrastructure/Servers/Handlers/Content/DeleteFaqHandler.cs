using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="DeleteFaq" /> with the rights of the sending connection.</summary>
internal sealed class DeleteFaqHandler(Server server) : IRequestHandler<DeleteFaq, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ServerOutcome>> Handle(DeleteFaq request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Faq.DeleteAsync(request.Entry);
	}
}