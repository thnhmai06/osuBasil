using Basil.Application.Sessions.Persistence;
using Basil.Domain.Sessions;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Session = Basil.Application.Sessions.Objects.Session;

namespace Basil.Application.Sessions.Objects;

public sealed class UserCredentials
{
	private readonly SessionContext _context;
	private readonly UserSessions _user;

	internal UserCredentials(UserSessions user, SessionContext context)
	{
		_user = user;
		_context = context;
	}

	internal Task<bool> IsPasswordAsync(Md5Hash passwordHash, CancellationToken ct = default)
	{
		return _context.Credentials.IsPasswordAsync(_user.Record, passwordHash, ct);
	}

	internal Task ReplaceAsync(Md5Hash passwordHash)
	{
		return _context.Credentials.CreateOrUpdateAsync(new Credentials(_user.Record, passwordHash));
	}

	internal async Task<UserActionResult> ChangeAsync(Md5Hash newPasswordHash, Md5Hash? currentPasswordHash,
		Session actor,
		bool includeCurrent)
	{
		var isSelf = actor.Record.Equals(_user.Record);
		var unchanged = false;
		if (isSelf)
		{
			if (currentPasswordHash is not { } current || !await IsPasswordAsync(current))
				return UserActionResult.WrongPassword;

			unchanged = current == newPasswordHash;
		}

		await ReplaceAsync(newPasswordHash);
		if (unchanged) return UserActionResult.Ok;

		var keep = isSelf && !includeCurrent && actor is ApiSession api ? api.State.Record.Id : (long?)null;
		await _user.CloseAsync(null, keep, SessionEndReason.CredentialsChanged);
		if (!isSelf)
			await _context.ModerationLog.AppendAsync(new ModerationLogData
			{
				Action = ModerationAction.ChangePassword,
				Actor = actor.Record,
				Target = _user.Record,
				Timestamp = _context.Clock.GetUtcNow()
			});

		return UserActionResult.Ok;
	}
}