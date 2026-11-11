using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to delete a stored screenshot.</summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Screenshot">The screenshot to delete.</param>
public sealed record DeleteScreenshot(Connection Actor, ContentReference Screenshot) : IRequest<ServerOutcome>;