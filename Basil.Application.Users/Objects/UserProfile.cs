using Basil.Domain.Users;
using Basil.Domain.Users;
using DomainProfile = Basil.Domain.Users.UserProfile;

namespace Basil.Application.Users.Objects;

public sealed class UserProfile
{
	private readonly User _user;

	internal UserProfile(User user)
	{
		_user = user;
	}

	public string? AwayMessage => _user.State.Profile.AwayMessage;

	public bool PmPrivate => _user.State.Profile.PmPrivate;

	internal void SetCountry(Country country)
	{
		_user.Apply(user => user.Profile.SetCountry(country));
	}

	internal void SetAway(string? message)
	{
		_user.State.Profile.SetAway(message);
	}

	internal void SetPmPrivate(bool pmPrivate)
	{
		_user.State.Profile.SetPmPrivate(pmPrivate);
	}

	internal async Task<UserOutcome> RenameAsync(string name)
	{
		if (DomainProfile.ValidateName(name) is var invalid and not UserOutcome.Ok) return invalid;

		return await _user.Context.Users.RenameAsync(_user.Record, name)
			? UserOutcome.Ok
			: UserOutcome.NameTaken;
	}

	internal async Task<UserOutcome> SetCountryAsync(Country country)
	{
		if (DomainProfile.ValidateCountry(country) is var invalid and not UserOutcome.Ok) return invalid;

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
		{
			if (stored is null)
				return UserOutcome.NotFound;
		}

		await _user.Context.Avatars.DeleteAsync(_user.Record);
		return UserOutcome.Ok;
	}
}