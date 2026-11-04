using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to rename an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Name">The new name.</param>
public sealed record RenameUser(Connection Actor, UserRecord Target, string Name) : IRequest<Result<UserOutcome>>;
