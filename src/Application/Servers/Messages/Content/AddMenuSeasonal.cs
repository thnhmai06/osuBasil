using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to add a seasonal background; an invalid name raises <see cref="ArgumentException" />.</summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Name">The name of the seasonal background.</param>
/// <param name="Image">The image of the seasonal background.</param>
public sealed record AddMenuSeasonal(Connection Actor, string Name, Stream Image)
	: IRequest<(Result<ServerOutcome> Result, ContentReference? Image)>;