using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record ListScreenshots(Connection Actor)
	: IRequest<(ServerOutcome Result, IReadOnlyList<ContentReference> Screenshots)>;