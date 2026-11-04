using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers for the stored screenshots.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record ListScreenshots(Connection Actor) : IRequest<(Result<ServerOutcome> Result, IReadOnlyList<ContentReference> Screenshots)>;
