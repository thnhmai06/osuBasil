using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Basil.Host.Shared.Http;

/// <summary>Sends a stored file to the client, handing the transfer to the reverse proxy when it is set up for that.</summary>
public static class StoredFile
{
	/// <summary>The setting that names the proxy location serving the data folder; unset means Basil serves the file itself.</summary>
	public const string AccelPrefixSetting = "Basil:Server:Files:AccelPrefix";

	private static readonly string DataRoot = Path.Combine(AppContext.BaseDirectory, "Data") + Path.DirectorySeparatorChar;

	/// <summary>
	///     Answers with the content of a file: the proxy streams it when the content is a file in the data folder and the
	///     proxy location is set, otherwise Basil does.
	/// </summary>
	public static IResult Send(HttpContext context, Stream content, string contentType, string? downloadName = null,
		DateTimeOffset? lastModified = null, EntityTagHeaderValue? entityTag = null, bool enableRangeProcessing = false)
	{
		var prefix = context.RequestServices.GetService<IConfiguration>()?[AccelPrefixSetting];
		if (!string.IsNullOrEmpty(prefix) && content is FileStream file &&
		    file.Name.StartsWith(DataRoot, StringComparison.Ordinal))
			return new Handoff(file, prefix, contentType, downloadName);

		return Results.File(content, contentType, downloadName, lastModified, entityTag, enableRangeProcessing);
	}

	private sealed class Handoff(FileStream file, string prefix, string contentType, string? downloadName) : IResult
	{
		public async Task ExecuteAsync(HttpContext context)
		{
			var relative = file.Name[DataRoot.Length..].Split(Path.DirectorySeparatorChar);
			await file.DisposeAsync();
			var headers = context.Response.Headers;
			headers.ContentType = contentType;
			if (downloadName is not null)
				headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
					{ FileNameStar = downloadName }.ToString();
			headers["X-Accel-Redirect"] = prefix.TrimEnd('/') + "/" + string.Join('/', relative.Select(Uri.EscapeDataString));
		}
	}
}
