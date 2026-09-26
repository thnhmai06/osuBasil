using Basil.Domain.Users;

namespace Basil.Application.Models.Configurations.Options;

public partial record BasilOptions
{
	/// <summary>
	///     Configuration for the built-in chat bot account that answers chat and <c>!mp</c> commands.
	/// </summary>
	/// <remarks>
	///     <see cref="BotBootstrapService" /> boots the seeded account row
	///     (id 0) into an in-memory session at startup and applies these options to it: the configured
	///     <see cref="Name" /> renames the row when it differs, and <see cref="Country" /> updates its
	///     stored country code. The name is configurable so an unrestricted rename cannot collide with
	///     the account's originally seeded row.
	/// </remarks>
	public sealed record BotOptions(string Name = "BasilBot", string Prefix = "!", Country Country = Country.Vn)
		: IConfiguration
	{
		public static string GetSectionName()
		{
			return BasilOptions.GetSectionName() + ":Bot";
		}
	}
}