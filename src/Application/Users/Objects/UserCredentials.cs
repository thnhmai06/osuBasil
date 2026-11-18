using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;

using Basil.Domain.Moderation;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Users.Objects;

public sealed class UserCredentials
{
	private readonly User _user;

	internal UserCredentials(User user)
	{
		_user = user;
	}

	internal Task<bool> IsPasswordAsync(Md5Hash passwordHash, CancellationToken ct = default)
	{
		return _user.Context.Credentials.IsPasswordAsync(_user.Record, passwordHash, ct);
	}

	internal Task ReplaceAsync(Md5Hash passwordHash)
	{
		return _user.Context.Credentials.CreateOrUpdateAsync(new Credentials(_user.Record, passwordHash));
	}

	internal async Task<UserOutcome> ChangeAsync(Md5Hash newPasswordHash, Md5Hash? currentPasswordHash, Connection actor,
		bool includeCurrent)
	{
		var isSelf = actor.Record.Equals(_user.Record);
		var unchanged = false;
		if (isSelf)
		{
			if (currentPasswordHash is not { } current || !await IsPasswordAsync(current))
				return UserOutcome.WrongPassword;

			unchanged = current == newPasswordHash;
		}

		await ReplaceAsync(newPasswordHash);
		if (unchanged) return UserOutcome.Ok;

		var keep = isSelf && !includeCurrent && actor is ApiConnection api ? api.Session.Id : (long?)null;
		await _user.Sessions.CloseAsync(null, keep, SessionEndReason.CredentialsChanged);
		if (!isSelf)
			await _user.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ChangePassword,
				Actor = actor.Record,
				Target = _user.Record,
				Timestamp = _user.Context.Clock.GetUtcNow()
			});

		return UserOutcome.Ok;
	}
}