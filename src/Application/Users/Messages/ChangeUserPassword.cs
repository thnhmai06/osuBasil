using Basil.Application.Shared;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to change the password of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="NewPasswordHash">The hash of the new password.</param>
/// <param name="CurrentPasswordHash">The hash of the current password, when the caller knows it.</param>
/// <param name="IncludeCurrent">True to record the current password in the moderation log.</param>
public sealed record ChangeUserPassword(
	Connection Actor,
	UserReference Target,
	Md5 NewPasswordHash,
	Md5? CurrentPasswordHash,
	bool IncludeCurrent) : IRequest<UserOutcome>;