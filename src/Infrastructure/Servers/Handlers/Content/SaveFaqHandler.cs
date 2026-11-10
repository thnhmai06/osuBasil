using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="SaveFaq" /> with the rights of the sending connection.</summary>
internal sealed class SaveFaqHandler(Server server) : IRequestHandler<SaveFaq, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ServerOutcome>> Handle(SaveFaq request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Content.SaveFaqAsync(request.Entry, request.Text);
	}
}