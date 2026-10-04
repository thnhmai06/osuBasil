using Basil.Domain.Client;
using Basil.Domain.Utilities;

namespace Basil.Application.Contracts.Scores;

/// <summary>The checksums of the beatmap a submission is checked against.</summary>
/// <param name="Hash">The MD5 of the beatmap file.</param>
/// <param name="StoryboardHash">The MD5 of the beatmap's storyboard, or <see langword="null" /> when it has none.</param>
public sealed record BeatmapChecksums(Md5 Hash, Md5? StoryboardHash);

/// <summary>What the osu! client sent about itself with a submission.</summary>
/// <param name="ClientHash">The client hash as sent, which the submission checksum covers.</param>
/// <param name="Fingerprint">The machine fingerprint the client hash describes.</param>
/// <param name="UninstallId">The client's uninstall id, as sent.</param>
/// <param name="DiskSignature">The client's disk signature, as sent.</param>
/// <param name="VersionDate">The client version date as sent, as <c>yyyyMMdd</c>, which the checksum covers.</param>
/// <param name="BeatmapHash">The MD5 of the beatmap the client claims to have played.</param>
public sealed record SubmittedClient(string ClientHash, ClientFingerprint Fingerprint, string UninstallId,
	string DiskSignature, string VersionDate, Md5 BeatmapHash);
