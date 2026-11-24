using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

public sealed class UserProfile
{
	private readonly User _user;

	internal UserProfile(User user)
	{
		_user = user;
	}

	public string? AwayMessage => _user.State.AwayMessage;

	public bool PmPrivate => _user.State.PmPrivate;

	internal void SetCountry(Country country)
	{
		_user.Apply(data => data.Country = country);
	}

	internal void SetAway(string? message)
	{
		_user.State.AwayMessage = message;
	}

	internal void SetPmPrivate(bool pmPrivate)
	{
		_user.State.PmPrivate = pmPrivate;
	}

	internal async Task<UserActionResult> RenameAsync(string name)
	{
		// Assigning the name applies the rules for user names before anything is stored.
		_ = new UserData { Name = name };

		return await _user.Context.Users.RenameAsync(_user.Record, name)
			? UserActionResult.Ok
			: UserActionResult.NameTaken;
	}

	internal async Task<UserActionResult> SetCountryAsync(Country country)
	{
		await _user.StoreChangeAsync(data => data.Country = country);
		SetCountry(country);
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> SetAvatarAsync(Stream image, CancellationToken ct)
	{
		var context = _user.Context;
		var limits = (await context.Settings.GetAsync(ct)).Uploads;

		await context.Avatars.SaveAsync(_user.Record, image, async (stored, token) =>
		{
			if (stored.Length > limits.AvatarMaxSize) throw new UploadTooLargeException(limits.AvatarMaxSize);

			var info = await context.Inspector.InspectAsync(stored, token);
			if (info is null || info.Width < 1 || info.Height < 1) throw new InvalidImageException();
			if (info.Width > limits.AvatarMaxSide || info.Height > limits.AvatarMaxSide)
				throw new ImageDimensionsTooLargeException(limits.AvatarMaxSide);
		}, ct);
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> RemoveAvatarAsync()
	{
		await using (var stored = await _user.Context.Avatars.OpenAsync(_user.Record.Id))
		{
			if (stored is null)
				return UserActionResult.NotFound;
		}

		await _user.Context.Avatars.DeleteAsync(_user.Record);
		return UserActionResult.Ok;
	}
}