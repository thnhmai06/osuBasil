using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record AddMenuBanner(
	Session Actor,
	string Name,
	Stream Image,
	Uri Url,
	DateTimeOffset? StartsAt,
	DateTimeOffset? EndsAt) : IRequest<(ServerActionResult Result, MenuBanner? Banner)>;