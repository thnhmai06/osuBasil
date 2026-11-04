using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="UpdateSettings" /> with the rights of the sending connection.</summary>
internal sealed class UpdateSettingsHandler(Server server) : IRequestHandler<UpdateSettings, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ServerOutcome>> Handle(UpdateSettings request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Settings.UpdateAsync(request.Settings, ct);
	}
}
