using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to delete a FAQ entry; an unsafe entry name raises <see cref="ArgumentException" />.</summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Entry">The name of the FAQ entry to delete.</param>
public sealed record DeleteFaq(Connection Actor, string Entry) : IRequest<Result<ServerOutcome>>;