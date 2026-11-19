namespace Basil.Domain.Authentication;

public static class SessionRules
{
	public static TimeSpan ReplaceableAfter => TimeSpan.FromSeconds(30);

	extension(SessionType type)
	{
		public TimeSpan ExpiresAfter => type == SessionType.Api ? TimeSpan.FromHours(8) : TimeSpan.FromSeconds(300);

		public bool IsExclusive => type != SessionType.StableTourney;
	}
}