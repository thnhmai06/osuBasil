using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Basil.Host.Shared.Http;

/// <summary>Sends a stored file to the client, handing the transfer to the reverse proxy when it is set up for that.</summary>
public static class StoredFile
{
	/// <summary>
	///     The setting that names the proxy location serving the Data and Cache folders, each below it under its own name
	///     (<c>/_files/Data/</c>, <c>/_files/Cache/</c>); unset means Basil serves the file itself.
	/// </summary>
	public const string AccelPrefixSetting = "Basil:Server:Files:AccelPrefix";

	private static readonly string[] Roots = ["Data", "Cache"];

	/// <summary>
	///     Answers with the content of a file: the proxy streams it when the content is a file in the Data or Cache folder and the
	///     proxy location is set, otherwise Basil does.
	/// </summary>
	public static IResult Send(HttpContext context, Stream content, string contentType, string? downloadName = null,
		DateTimeOffset? lastModified = null, EntityTagHeaderValue? entityTag = null, bool enableRangeProcessing = false)
	{
		var prefix = context.RequestServices.GetService<IConfiguration>()?[AccelPrefixSetting];
		if (!string.IsNullOrEmpty(prefix) && content is FileStream file && RootOf(file.Name) is { } root)
			return new Handoff(file, root, prefix, contentType, downloadName);

		return Results.File(content, contentType, downloadName, lastModified, entityTag, enableRangeProcessing);
	}

	private static string? RootOf(string path)
	{
		return Roots.FirstOrDefault(root => path.StartsWith(
			Path.Combine(AppContext.BaseDirectory, root) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
	}

	private sealed class Handoff(FileStream file, string root, string prefix, string contentType, string? downloadName)
		: IResult
	{
		public async Task ExecuteAsync(HttpContext context)
		{
			var relative = file.Name[(Path.Combine(AppContext.BaseDirectory, root).Length + 1)..]
				.Split(Path.DirectorySeparatorChar);
			await file.DisposeAsync();
			var headers = context.Response.Headers;
			headers.ContentType = contentType;
			if (downloadName is not null)
				headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
					{ FileNameStar = downloadName }.ToString();
			headers["X-Accel-Redirect"] = prefix.TrimEnd('/') + "/" + root + "/" + string.Join('/', relative.Select(Uri.EscapeDataString));
		}
	}
}
