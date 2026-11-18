using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Users.Objects;

public sealed class UserProfile
{
	private readonly User _user;
	private string? _awayMessage;
	private bool _pmPrivate;

	internal UserProfile(User user)
	{
		_user = user;
	}

	public string? AwayMessage
	{
		get
		{
			using var scope = _user.Enter();
			return _awayMessage;
		}
	}

	public bool PmPrivate
	{
		get
		{
			using var scope = _user.Enter();
			return _pmPrivate;
		}
	}

	internal void SetCountry(Country country)
	{
		using var scope = _user.Enter();
		_user.Change(data => data.Country = country);
	}

	internal void SetAway(string? message)
	{
		using var scope = _user.Enter();
		_awayMessage = string.IsNullOrWhiteSpace(message) ? null : message;
	}

	internal void SetPmPrivate(bool pmPrivate)
	{
		using var scope = _user.Enter();
		_pmPrivate = pmPrivate;
	}

	internal async Task<UserOutcome> RenameAsync(string name)
	{
		if (!UserData.IsValidName(name)) return UserOutcome.InvalidName;

		return await _user.Context.Users.RenameAsync(_user.Record, name)
			? UserOutcome.Ok
			: UserOutcome.NameTaken;
	}

	internal async Task<UserOutcome> SetCountryAsync(Country country)
	{
		if (!Enum.IsDefined(country)) return UserOutcome.InvalidCountry;

		await _user.StoreChangeAsync(data => data.Country = country);
		SetCountry(country);
		return UserOutcome.Ok;
	}

	internal async Task<UserOutcome> SetAvatarAsync(Stream image, CancellationToken ct)
	{
		var context = _user.Context;
		var limits = (await context.Settings.GetAsync(ct)).Uploads;

		await using var upload = await context.Spool.SpoolAsync(image, limits.AvatarMaxSize, ct);
		if (upload.TooLarge) return UserOutcome.TooLarge;

		var info = await context.Inspector.InspectAsync(upload.Content);
		if (info is null || info.Width < 1 || info.Height < 1)
		{
			await upload.QuarantineAsync();
			return UserOutcome.InvalidImage;
		}

		if (info.Width > limits.AvatarMaxSide || info.Height > limits.AvatarMaxSide)
		{
			await upload.QuarantineAsync();
			return UserOutcome.DimensionsTooLarge;
		}

		upload.Content.Position = 0;
		await context.Avatars.SaveAsync(_user.Record, upload.Content);
		return UserOutcome.Ok;
	}

	internal async Task<UserOutcome> RemoveAvatarAsync()
	{
		await using (var stored = await _user.Context.Avatars.OpenAsync(_user.Record))
			if (stored is null)
				return UserOutcome.NotFound;

		await _user.Context.Avatars.DeleteAsync(_user.Record);
		return UserOutcome.Ok;
	}
}