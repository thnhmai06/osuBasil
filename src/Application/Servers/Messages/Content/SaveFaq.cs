using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to create or replace a FAQ entry; an unsafe entry name raises <see cref="ArgumentException" />.</summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Entry">The name of the FAQ entry to create or replace.</param>
/// <param name="Text">The text of the FAQ entry.</param>
public sealed record SaveFaq(Connection Actor, string Entry, string Text) : IRequest<ServerOutcome>;