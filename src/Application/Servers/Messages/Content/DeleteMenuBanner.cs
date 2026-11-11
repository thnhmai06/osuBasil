using Basil.Application.Shared;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to delete a main-menu banner and its image.</summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Banner">The banner to delete.</param>
public sealed record DeleteMenuBanner(Connection Actor, MenuBanner Banner) : IRequest<ServerOutcome>;