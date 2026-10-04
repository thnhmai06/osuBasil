namespace Basil.Application.Contracts.Scores;

/// <summary>The checksums of the beatmap a submission is checked against.</summary>
/// <param name="Hash">The MD5 of the beatmap file.</param>
/// <param name="StoryboardHash">The MD5 of the beatmap's storyboard, or <see langword="null" /> when it has none.</param>
public sealed record BeatmapChecksums(Md5 Hash, Md5? StoryboardHash);

/// <summary>What the osu! client sent about itself with a submission.</summary>
/// <param name="FingerprintHash">The client hash.</param>
/// <param name="Serial">The client's unique ids, separated by <c>|</c>.</param>
/// <param name="VersionDate">The client version date, as <c>yyyyMMdd</c>.</param>
/// <param name="BeatmapHash">The MD5 of the beatmap the client claims to have played.</param>
public sealed record SubmittedClient(string FingerprintHash, string Serial, string VersionDate, Md5 BeatmapHash);