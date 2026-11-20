using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SearchUsers(Session Actor, UserQuery Query, PageRequest Page) : IRequest<Page<UserRecord>>;