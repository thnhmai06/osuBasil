using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record NotifyEveryone(Session Actor, string Text) : IRequest<(ServerActionResult Result, int Sent)>;