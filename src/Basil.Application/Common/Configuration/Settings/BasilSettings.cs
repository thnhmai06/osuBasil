using Basil.Application.Common.Configuration;
namespace Basil.Application.Common.Configuration.Settings;

public sealed partial class BasilSettings : IConfiguration
{
	public required AdminKeySettings AdminKey { get; init; }

	public required MenuIconSettings MenuIcon { get; init; }

	public required MotdSettings Motd { get; init; }

	public required MirrorSettings Mirror { get; init; }

	public static string GetSectionName()
	{
		return "Basil";
	}
}