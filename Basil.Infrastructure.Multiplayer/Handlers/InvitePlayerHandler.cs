using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Sessions.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class InvitePlayerHandler(SessionRegistry registry) : IRequestHandler<InvitePlayer, RoomInviteResult>
{
	public async ValueTask<RoomInviteResult> Handle(InvitePlayer request, CancellationToken ct)
	{
		var members = request.Room.ScopeTo(request.Actor).Members;
		if (members.RequireInvite() is { } refusal) return refusal;

		var target = registry.Online.FirstOrDefault(pair => pair.Key.Id == request.TargetId).Value;
		return await members.InviteAsync(target);
	}
}