using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to delete a seasonal background.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record DeleteMenuSeasonal(Connection Actor, ContentReference Image) : IRequest<Result<ServerOutcome>>;
