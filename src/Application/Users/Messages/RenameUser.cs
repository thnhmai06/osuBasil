using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record RenameUser(Connection Actor, UserReference Target, string Name) : IRequest<UserOutcome>;