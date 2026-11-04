using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

/// <summary>Asks the server to create and open a general channel.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Record">The channel to create.</param>
/// <remarks>The answer is <see cref="ServerOutcome.NameTaken" /> when a channel with that name is already open.</remarks>
public sealed record CreateGeneralChannel(Connection Actor, GeneralChannelRecord Record)
	: IRequest<Result<ServerOutcome>>;
