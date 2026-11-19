using Basil.Domain.Content;
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

	internal async Task<UserActionResult> RenameAsync(string name)
	{
		DomainProfile.ValidateName(name);

		return await _user.Context.Users.RenameAsync(_user.Record, name)
			? UserActionResult.Ok
			: UserActionResult.NameTaken;
	}

	internal async Task<UserActionResult> SetCountryAsync(Country country)
	{
		DomainProfile.ValidateCountry(country);

		await _user.StoreChangeAsync(data => data.Country = country);
		SetCountry(country);
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> SetAvatarAsync(Stream image, CancellationToken ct)
	{
		var context = _user.Context;
		var limits = (await context.Settings.GetAsync(ct)).Uploads;

		await using var upload = await context.Spool.SpoolAsync(image, limits.AvatarMaxSize, ct);
		if (upload.TooLarge) throw new UploadTooLargeException(limits.AvatarMaxSize);

		var info = await context.Inspector.InspectAsync(upload.Content);
		if (info is null || info.Width < 1 || info.Height < 1)
		{
			await upload.QuarantineAsync();
			throw new InvalidImageException();
		}

		if (info.Width > limits.AvatarMaxSide || info.Height > limits.AvatarMaxSide)
		{
			await upload.QuarantineAsync();
			throw new ImageDimensionsTooLargeException(limits.AvatarMaxSide);
		}

		upload.Content.Position = 0;
		await context.Avatars.SaveAsync(_user.Record, upload.Content);
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> RemoveAvatarAsync()
	{
		await using (var stored = await _user.Context.Avatars.OpenAsync(_user.Record))
		{
			if (stored is null)
				return UserActionResult.NotFound;
		}

		await _user.Context.Avatars.DeleteAsync(_user.Record);
		return UserActionResult.Ok;
	}
}