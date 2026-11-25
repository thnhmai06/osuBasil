using Basil.Application.Sessions.Objects;
using Basil.Domain.Chat;
using Mediator;

namespace Basil.Application.Chat.Messages;

/// <summary>Asks for the general channels an actor may see, ordered by name.</summary>
/// <param name="Actor">The session asking.</param>
public sealed record ListChannels(Session Actor) : IRequest<IReadOnlyList<GeneralChannelRecord>>;
