using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

/// <summary>Asks the server to change the settings of a general channel.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Name">The name of the channel.</param>
/// <param name="Change">The settings to change.</param>
/// <remarks>
///     The privilege to manage channels is checked before the name is looked up, so a refusal is the same whether or not
///     the
///     channel exists. An invalid change throws <see cref="ArgumentException" />.
/// </remarks>
public sealed record ConfigureGeneralChannel(Connection Actor, string Name, GeneralChannelChange Change)
	: IRequest<GeneralChannelAnswer>;

/// <summary>The answer to a request that acts on a named general channel.</summary>
/// <param name="Result">The outcome; NotFound when no open channel has the name.</param>
/// <param name="Channel">The channel, or <see langword="null" /> when it was not found or the actor was refused.</param>
public sealed record GeneralChannelAnswer(ChannelOutcome Result, GeneralChannel? Channel);