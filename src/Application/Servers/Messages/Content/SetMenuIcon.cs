using Basil.Application.Shared;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>
///     Asks Servers to set the main-menu icon: its image, the address it links to, or both; an invalid name raises
///     <see cref="ArgumentException" />.
/// </summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Name">The name of the new icon image, set together with Image.</param>
/// <param name="Image">The new icon image, or null to keep the current one.</param>
/// <param name="Url">The address the icon links to, or null for no link.</param>
public sealed record SetMenuIcon(Connection Actor, string? Name, Stream? Image, Uri? Url)
	: IRequest<ServerOutcome>;