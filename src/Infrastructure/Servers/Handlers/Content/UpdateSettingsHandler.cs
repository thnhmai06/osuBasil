using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="UpdateSettings" /> with the privileges of the sending connection.</summary>
internal sealed class UpdateSettingsHandler(Server server) : IRequestHandler<UpdateSettings, ServerOutcome>
{
	/// <inheritdoc />
	public async ValueTask<ServerOutcome> Handle(UpdateSettings request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Settings.UpdateAsync(request.Settings);
	}
}