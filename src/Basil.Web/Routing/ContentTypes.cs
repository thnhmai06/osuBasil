using Microsoft.AspNetCore.StaticFiles;

namespace Basil.Web.Routing;

/// <summary>
///     Resolves MIME types from file extensions, including osu!-specific file formats.
/// </summary>
internal static class ContentTypes
{
	private static readonly FileExtensionContentTypeProvider Provider = BuildProvider();

	private static FileExtensionContentTypeProvider BuildProvider()
	{
		var provider = new FileExtensionContentTypeProvider
		{
			Mappings =
			{
				[".osu"] = "application/x-osu-beatmap",
				[".osr"] = "application/x-osu-replay",
				[".osk"] = "application/x-osu-skin",
				[".osb"] = "application/x-osu-storyboard",
				[".osz"] = "application/x-osu-beatmap-archive",
				[".osz2"] = "application/x-osu-beatmap-archive"
			}
		};
		return provider;
	}

	/// <summary>
	///     Resolves the MIME type for a file path.
	/// </summary>
	/// <param name="path">The file path or filename.</param>
	/// <param name="fallback">
	///     The MIME type to return if the file extension is not recognized.
	/// </param>
	/// <returns>
	///     The resolved MIME type, or <paramref name="fallback" /> if the extension is unknown.
	/// </returns>
	public static string Resolve(string path, string fallback = "application/octet-stream")
	{
		return Provider.TryGetContentType(path, out var contentType) ? contentType : fallback;
	}
}