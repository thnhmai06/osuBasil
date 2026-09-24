using System.Text.RegularExpressions;
using Basil.Domain.Client;

namespace Basil.Domain.Users;

/// <summary>
///     Represents a registered user of the server.
/// </summary>
public sealed partial class User : IEquatable<User>
{
	/// <summary>Gets the unique identifier of the user.</summary>
	public required int Id { get; init; }

	/// <summary>
	///     Gets or sets the username.
	/// </summary>
	/// <exception cref="ArgumentException">
	///     Thrown when the username does not satisfy osu!'s registration rules.
	/// </exception>
	public required string Name
	{
		get;
		set
		{
			if (value.Length is < 3 or > 15)
				throw new ArgumentException("Username must be between 3 and 15 characters.", nameof(value));
			if (value.StartsWith(' ') || value.EndsWith(' '))
				throw new ArgumentException("Username cannot start or end with a space.", nameof(value));
			if (value.Contains("  "))
				throw new ArgumentException("Username cannot contain consecutive spaces.", nameof(value));
			if (value.All(char.IsDigit))
				throw new ArgumentException("Username cannot contain only digits.", nameof(value));
			if (!OsuUsernameChars().IsMatch(value))
				throw new ArgumentException("Username may only contain letters, numbers, spaces, and _ - [ ].",
					nameof(value));
			field = value;
		}
	}

	/// <summary>Gets or sets the country the user is registered in.</summary>
	public Country Country { get; set; } = Country.Xx;

	/// <summary>
	///     Gets or sets the privileges granted to the user.
	/// </summary>
	/// <remarks>
	///     The getter reports <see cref="ClientPrivileges.None" /> when the user is deleted and the
	///     stored value otherwise. The setter stores <see cref="ClientPrivileges.None" /> when the
	///     user is not deleted and the assigned value when the user is deleted, so the privileges of
	///     a live user are effectively fixed by the property initializer. New users default to
	///     <see cref="ClientPrivileges.Player" /> combined with
	///     <see cref="ClientPrivileges.Supporter" />.
	/// </remarks>
	public ClientPrivileges Privilege
	{
		get => DeletedAt is not null ? ClientPrivileges.None : field;
		set => field = DeletedAt is null ? ClientPrivileges.None : value;
	} = ClientPrivileges.Player | ClientPrivileges.Supporter;

	/// <summary>
	///     Gets or sets the date and time when the user's silence expires, if the user is silenced.
	/// </summary>
	/// <remarks>
	///     The osu! client enforces the silence itself once it is told about it.
	/// </remarks>
	public DateTimeOffset? SilenceEnd { get; set; } = null;

	/// <summary>Gets or sets the date and time when the user was deleted, if any.</summary>
	public DateTimeOffset? DeletedAt { get; set; } = null;

	[GeneratedRegex(@"^[a-zA-Z0-9_\-\[\] ]+$")]
	private static partial Regex OsuUsernameChars();

	/// <summary>
	///     Determines whether another user refers to the same account.
	/// </summary>
	/// <remarks>
	///     Two users are considered equal when their <see cref="Id" /> values are equal.
	/// </remarks>
	/// <param name="other">The user to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> has the same <see cref="Id" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(User? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>
	///     Determines whether this user equals another object.
	/// </summary>
	/// <param name="obj">The object to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="User" /> with the same
	///     <see cref="Id" />; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is User other && Equals(other);
	}

	/// <summary>
	///     Returns the hash code of this user.
	/// </summary>
	/// <returns>The <see cref="Id" />, which uniquely identifies the user.</returns>
	public override int GetHashCode()
	{
		return Id;
	}
}