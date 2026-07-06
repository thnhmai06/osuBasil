namespace Bancho.Application.Abstractions;

/// <summary>Ported from app/encryption.py's decrypt_score_aes_data.</summary>
public interface IScoreDecryptor
{
    /// <summary>
    /// Decrypts the osu! client's Rijndael-256-CBC-encrypted submission payload. Returns the
    /// colon-delimited score-data fields (split by the caller, matching Python's own
    /// `.split(":")`) alongside the decrypted client hash string.
    /// </summary>
    (string[] ScoreDataFields, string ClientHash) Decrypt(
        string scoreDataBase64,
        string clientHashBase64,
        string ivBase64,
        string osuVersion);
}
