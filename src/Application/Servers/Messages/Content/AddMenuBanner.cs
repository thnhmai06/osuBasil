using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record AddMenuBanner(
	Connection Actor,
	string Name,
	Stream Image,
	Uri Url,
	DateTimeOffset? StartsAt,
	DateTimeOffset? EndsAt) : IRequest<(ServerOutcome Result, MenuBanner? Banner)>;