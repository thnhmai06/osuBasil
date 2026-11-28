using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>Asks whether a session may change the help entries, so a refused change is not read at all.</summary>
public sealed record MayChangeFaq(Session Actor) : IRequest<ServerActionResult>;
