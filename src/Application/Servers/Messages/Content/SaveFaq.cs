using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to create or replace a FAQ entry; an unsafe entry name raises <see cref="ArgumentException" />.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record SaveFaq(Connection Actor, string Entry, string Text) : IRequest<Result<ServerOutcome>>;
