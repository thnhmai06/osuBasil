using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="SaveFaq" /> with the privileges of the sending connection.</summary>
internal sealed class SaveFaqHandler(Server server) : IRequestHandler<SaveFaq, ServerOutcome>
{
	/// <inheritdoc />
	public async ValueTask<ServerOutcome> Handle(SaveFaq request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Faq.SaveAsync(request.Entry, request.Text);
	}
}