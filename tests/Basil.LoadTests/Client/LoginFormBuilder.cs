using System.Text;
using Basil.LoadTests.Helpers;
using Basil.LoadTests.Models;

namespace Basil.LoadTests.Client;

/// <summary>
///     Builds the raw bancho login body:
///     <c>{username}\n{passwordMd5}\n{osuVersion}|{utcOffset}|{displayCity}|{clientHashes}|{pmPrivate}\n</c>,
///     per <c>LoginForm.From</c> (<c>src/Basil.Application/Abstractions/Login/LoginForm.cs</c>) — field
///     order verified directly against that parser, since the docs and adjacent notes disagree on it.
/// </summary>
public static class LoginFormBuilder
{
	/// <summary>
	///     A fixed, always-valid client version. <c>b</c> + an 8-digit date parses under
	///     <c>OsuVersion</c>'s regex and <c>DateTime.ParseExact("yyyyMMdd")</c> without needing a real
	///     osu! release date.
	/// </summary>
	private const string OsuVersion = "b20231231";

	/// <summary>Builds the UTF-8 login body for <paramref name="account" />.</summary>
	public static byte[] Build(LoadAccount account)
	{
		var clientHashes = HardwareHashes.ForAccount(account.Index);
		var body = $"{account.Name}\n{account.PasswordMd5}\n{OsuVersion}|0|0|{clientHashes}|0\n";
		return Encoding.UTF8.GetBytes(body);
	}
}