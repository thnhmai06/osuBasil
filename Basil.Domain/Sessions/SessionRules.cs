namespace Basil.Domain.Sessions;

/// <summary>The time limits and type rules that decide how a session ages.</summary>
public static class SessionRules
{
	/// <summary>How long a session may stay silent before a new sign-in of the same type may take its place.</summary>
	public static TimeSpan ReplaceableAfter => TimeSpan.FromSeconds(30);

	extension(SessionType type)
	{
		/// <summary>How long a session of this type may stay silent before it expires: 8 hours for an API session, 300 seconds for any other.</summary>
		public TimeSpan ExpiresAfter => type == SessionType.Api ? TimeSpan.FromHours(8) : TimeSpan.FromSeconds(300);

		/// <summary>Whether a user may hold only one live session of this type; every type but <see cref="SessionType.StableTourney" /> is.</summary>
		public bool IsExclusive => type != SessionType.StableTourney;
	}
}