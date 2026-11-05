using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>Asks Servers to delete a stored screenshot.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record DeleteScreenshot(Connection Actor, ContentReference Screenshot) : IRequest<Result<ServerOutcome>>;