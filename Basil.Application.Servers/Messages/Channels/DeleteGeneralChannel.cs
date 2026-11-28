using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

public sealed record DeleteGeneralChannel(Session Actor, string Name) : IRequest<GeneralChannelAnswer>;