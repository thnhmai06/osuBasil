using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>
///     Asks Servers to add a main-menu banner whose image is uploaded; an invalid name raises
///     <see cref="ArgumentException" />.
/// </summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Name">The name of the banner.</param>
/// <param name="Image">The image shown on the banner.</param>
/// <param name="Url">The address the banner links to.</param>
/// <param name="StartsAt">The time the banner starts to show, or null for no start.</param>
/// <param name="EndsAt">The time the banner stops showing, or null for no end.</param>
public sealed record AddMenuBanner(
	Connection Actor,
	string Name,
	Stream Image,
	Uri Url,
	DateTimeOffset? StartsAt,
	DateTimeOffset? EndsAt) : IRequest<(ServerOutcome Result, MenuBanner? Banner)>;