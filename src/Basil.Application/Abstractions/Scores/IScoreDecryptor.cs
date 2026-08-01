namespace Basil.Application.Abstractions.Scores;

/// <summary>
///     Decrypts the payload the osu! client submits with a score.
/// </summary>
/// <remarks>
///     The client encrypts its score data and its client hash with a symmetric cipher derived from
///     the osu! version string. This is genuinely Rijndael, not AES: the client uses a 256-bit
///     block size, which the .NET <c>Aes</c> class cannot handle because it is hardcoded to 128-bit
///     blocks, so the Infrastructure implementation uses BouncyCastle's configurable-block
///     Rijndael engine.
/// </remarks>
public interface IScoreDecryptor
{
	/// <summary>
	///     Decrypts a score submission payload.
	/// </summary>
	/// <param name="scoreDataBase64">The base64-encoded encrypted score data.</param>
	/// <param name="clientHashBase64">The base64-encoded encrypted client hash.</param>
	/// <param name="ivBase64">The base64-encoded initialization vector.</param>
	/// <param name="osuVersion">The osu! version string of the submitting client.</param>
	/// <returns>
	///     The decrypted score data split into its colon-delimited fields, alongside the decrypted
	///     client hash string.
	/// </returns>
	/// <remarks>
	///     The score data is returned as the raw colon-delimited fields; splitting on the delimiter
	///     is the caller's job. Both decrypted values come back as plain strings.
	/// </remarks>
	(string[] ScoreDataFields, string ClientHash) Decrypt(
		string scoreDataBase64,
		string clientHashBase64,
		string ivBase64,
		string osuVersion);
}