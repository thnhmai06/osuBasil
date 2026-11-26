using System.Security.Cryptography;
using System.Text;

namespace Basil.Tests.Shared.Clients.Stable;

/// <summary>
///     Who an osu! client is: the name and password of its account and the install it reports, fixed for the life of
///     one virtual user.
/// </summary>
/// <remarks>
///     The same identity must sign in and submit scores: the server compares the version, the client hash and the
///     uninstall and disk values of the two.
/// </remarks>
/// <param name="Name">The exact account name, as the account was created.</param>
/// <param name="Password">The account password.</param>
/// <param name="Version">The client build date as eight digits.</param>
/// <param name="Tourney">Whether the client is the osu!tourney client.</param>
/// <param name="PathHash">The md5 of the install path the client reports.</param>
/// <param name="Adapters">The network adapters text the client reports.</param>
/// <param name="AdaptersHash">The md5 the client reports for the adapters.</param>
/// <param name="UninstallId">
///     The raw uninstall identifier; the client reports its md5 at sign-in and the raw value in
///     score submissions.
/// </param>
/// <param name="DiskSerial">The raw disk signature; handled like the uninstall identifier.</param>
public sealed record StableIdentity(
	string Name,
	string Password,
	string Version,
	bool Tourney,
	string PathHash,
	string Adapters,
	string AdaptersHash,
	string UninstallId,
	string DiskSerial)
{
	/// <summary>The build date the clients report unless told otherwise.</summary>
	public const string DefaultVersion = "20250101";

	/// <summary>Gets the md5 of the password, which is what every sign-in and web call sends.</summary>
	public string PasswordMd5 => Md5(Password);

	/// <summary>Gets the build as the client writes it at sign-in, such as <c>b20250101</c> or <c>b20250101tourney</c>.</summary>
	public string VersionText => $"b{Version}{(Tourney ? "tourney" : string.Empty)}";

	/// <summary>
	///     Gets the client hash text of the sign-in: the path hash, adapters, adapters hash, uninstall hash and disk
	///     hash.
	/// </summary>
	public string ClientHash => $"{PathHash}:{Adapters}:{AdaptersHash}:{Md5(UninstallId)}:{Md5(DiskSerial)}:";

	/// <summary>Gets the raw <c>uninstall|disk</c> pair a score submission carries.</summary>
	public string ClientSerial => $"{UninstallId}|{DiskSerial}";

	/// <summary>
	///     Creates the identity of a virtual user; every hash is derived from the name, so the same name always gives the
	///     same install.
	/// </summary>
	/// <param name="name">The exact account name.</param>
	/// <param name="password">The account password.</param>
	/// <param name="tourney">Whether the client signs in as the osu!tourney client.</param>
	public static StableIdentity Create(string name, string password, bool tourney = false)
	{
		return new StableIdentity(name, password, DefaultVersion, tourney, Md5($"{name}-path"), "runningunderwine",
			Md5($"{name}-adapters"), $"{name}-uninstall", $"{name}-disk");
	}

	/// <summary>Writes the body of the sign-in request.</summary>
	public string LoginBody()
	{
		return $"{Name}\n{PasswordMd5}\n{VersionText}|0|0|{ClientHash}|0\n";
	}

	/// <summary>Gets the lower-case hex md5 of a text.</summary>
	public static string Md5(string text)
	{
		return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));
	}
}