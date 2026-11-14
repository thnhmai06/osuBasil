using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>Owns the profile settings of one account that are kept in memory while it is online.</summary>
public sealed class UserProfile
{
	private readonly User _user;
	private string? _awayMessage;
	private bool _pmPrivate;

	/// <summary>Creates the profile state for an account.</summary>
	/// <param name="user">The account that owns the profile.</param>
	internal UserProfile(User user)
	{
		_user = user;
	}

	/// <summary>Gets the away message shown to other users, or <see langword="null" /> when the user is not away.</summary>
	public string? AwayMessage
	{
		get
		{
			using var scope = _user.Enter();
			return _awayMessage;
		}
	}

	/// <summary>Gets whether the user accepts private messages only from friends.</summary>
	public bool PmPrivate
	{
		get
		{
			using var scope = _user.Enter();
			return _pmPrivate;
		}
	}

	/// <summary>Sets the country shown on the profile.</summary>
	/// <param name="country">The country.</param>
	internal void SetCountry(Country country)
	{
		using var scope = _user.Enter();
		_user.Change(data => data.Country = country);
	}

	/// <summary>Sets the away message shown to other users.</summary>
	/// <param name="message">The message, or <see langword="null" /> or whitespace to clear it.</param>
	internal void SetAway(string? message)
	{
		using var scope = _user.Enter();
		_awayMessage = string.IsNullOrWhiteSpace(message) ? null : message;
	}

	/// <summary>Sets whether the user accepts private messages only from friends.</summary>
	/// <param name="pmPrivate"><see langword="true" /> to restrict private messages to friends.</param>
	internal void SetPmPrivate(bool pmPrivate)
	{
		using var scope = _user.Enter();
		_pmPrivate = pmPrivate;
	}

	/// <summary>Gives the account a new name.</summary>
	/// <param name="name">The new name.</param>
	/// <returns>The outcome of renaming the account.</returns>
	internal async Task<UserOutcome> RenameAsync(string name)
	{
		if (!UserData.IsValidName(name)) return UserOutcome.InvalidName;

		return await _user.Context.Users.RenameAsync(_user.Record, name)
			? UserOutcome.Ok
			: UserOutcome.NameTaken;
	}

	/// <summary>Stores and sets the country shown on the profile.</summary>
	/// <param name="country">The country.</param>
	/// <returns>The outcome of setting the country.</returns>
	internal async Task<UserOutcome> SetCountryAsync(Country country)
	{
		if (!Enum.IsDefined(country)) return UserOutcome.InvalidCountry;

		await _user.StoreChangeAsync(data => data.Country = country);
		SetCountry(country);
		return UserOutcome.Ok;
	}

	/// <summary>Replaces the avatar image.</summary>
	/// <param name="image">The image, read from its current position; not disposed.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome of setting the avatar.</returns>
	/// <remarks>A refused image leaves the previous avatar untouched.</remarks>
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

	/// <summary>Removes the avatar image, so the default avatar shows.</summary>
	/// <returns>The outcome of removing the avatar.</returns>
	internal async Task<UserOutcome> RemoveAvatarAsync()
	{
		await _user.Context.Avatars.DeleteAsync(_user.Record);
		return UserOutcome.Ok;
	}
}