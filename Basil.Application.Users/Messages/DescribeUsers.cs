using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks for the standing of accounts: the privileges each holds now and until when each is silenced.</summary>
/// <param name="Users">The accounts to describe.</param>
/// <remarks>The answer keeps the order of <paramref name="Users"/>.</remarks>
public sealed record DescribeUsers(IReadOnlyList<UserRecord> Users) : IRequest<IReadOnlyList<UserStanding>>;

/// <summary>An account with the privileges it holds now and the end of its silence.</summary>
/// <param name="Record">The account.</param>
/// <param name="EffectivePrivileges">The privileges the account holds now.</param>
/// <param name="SilencedUntil">When the silence ends, or <see langword="null"/> when the account is not silenced.</param>
public sealed record UserStanding(UserRecord Record, UserPrivileges EffectivePrivileges, DateTimeOffset? SilencedUntil);
