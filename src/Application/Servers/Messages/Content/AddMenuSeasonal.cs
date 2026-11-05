using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>Asks Servers to add a seasonal background; an invalid name raises <see cref="ArgumentException" />.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record AddMenuSeasonal(Connection Actor, string Name, Stream Image)
	: IRequest<(Result<ServerOutcome> Result, ContentReference? Image)>;