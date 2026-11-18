using Basil.Domain.Authentication;

namespace Basil.Application.Users.Objects.Connections;

public static class ConnectionRules
{
	extension(SessionType type)
	{
		public bool ReplacesOnJoin()
		{
			return type is SessionType.Stable;
		}

		internal TimeSpan IdleTimeout()
		{
			return TimeSpan.FromSeconds(300);
		}
	}
}