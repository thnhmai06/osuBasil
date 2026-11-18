using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record ListScreenshots(Session Actor)
	: IRequest<(ServerOutcome Result, IReadOnlyList<ContentReference> Screenshots)>;