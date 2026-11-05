using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

/// <summary>Asks the server to close and delete a general channel.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Name">The name of the channel.</param>
/// <remarks>
///     The right to manage channels is checked before the name is looked up, so a refusal is the same whether or not the
///     channel exists.
/// </remarks>
public sealed record DeleteGeneralChannel(Connection Actor, string Name) : IRequest<GeneralChannelAnswer>;