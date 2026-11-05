using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>
///     Asks Servers to add a main-menu banner whose image is uploaded; an invalid name raises
///     <see cref="ArgumentException" />.
/// </summary>
/// <param name="Actor">The connection acting.</param>
public sealed record AddMenuBanner(
	Connection Actor,
	string Name,
	Stream Image,
	Uri Url,
	DateTimeOffset? StartsAt,
	DateTimeOffset? EndsAt) : IRequest<(Result<ServerOutcome> Result, MenuBanner? Banner)>;