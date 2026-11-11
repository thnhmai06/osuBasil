using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to notify every open osu! client.</summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Text">The text of the notice.</param>
public sealed record NotifyEveryone(Connection Actor, string Text) : IRequest<(ServerOutcome Result, int Sent)>;