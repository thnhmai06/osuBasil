using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users for the public profile of one account.</summary>
/// <param name="Target">The account to read.</param>
/// <remarks>The answer is <see langword="null" /> when no account has the reference or the account was deleted.</remarks>
public sealed record GetUser(UserReference Target) : IRequest<UserRecord?>;