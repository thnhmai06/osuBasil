using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>
///     Asks Servers to set the main-menu icon: its image, the address it links to, or both; an invalid name raises
///     <see cref="ArgumentException" />.
/// </summary>
/// <param name="Actor">The connection acting.</param>
public sealed record SetMenuIcon(Connection Actor, string? Name, Stream? Image, Uri? Url)
	: IRequest<Result<ServerOutcome>>;