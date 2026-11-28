using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Basil.Host.Shared.Http;

/// <summary>A multipart upload read as a stream: its small text fields and the one file, which is never buffered in memory.</summary>
public sealed class UploadForm : IAsyncDisposable
{
	private const int MaxFields = 32;

	private const int MaxFieldLength = 16 * 1024;

	private UploadForm(IReadOnlyDictionary<string, string> fields, string? fileName, Stream? file)
	{
		Fields = fields;
		FileName = fileName;
		File = file;
	}

	/// <summary>The text fields, by name.</summary>
	public IReadOnlyDictionary<string, string> Fields { get; }

	/// <summary>The name the sender gave the file, or null when there is no file.</summary>
	public string? FileName { get; }

	/// <summary>The file's bytes, checked and counted as they are read; null when the form has no such file.</summary>
	public Stream? File { get; }

	public ValueTask DisposeAsync()
	{
		return File?.DisposeAsync() ?? ValueTask.CompletedTask;
	}

	/// <summary>Gets a text field, or an empty string when the sender gave none.</summary>
	public string Field(string name)
	{
		return Fields.GetValueOrDefault(name, string.Empty);
	}

	/// <summary>Reads the form of an upload request up to its file.</summary>
	/// <param name="context">The request, already bounded by <see cref="UploadRequest.Accept" />.</param>
	/// <param name="fileField">The name of the file field.</param>
	/// <param name="signature">What the file must begin with.</param>
	/// <param name="fileLimit">The most bytes the file may have.</param>
	/// <param name="fieldsBeforeFile">The text fields the caller needs; the file is handed over as the request's own stream only when all of them came before it.</param>
	/// <param name="seekable">Whether the file must be seekable with a known length, which gives it a bounded temporary file.</param>
	/// <param name="ct">Cancels the read.</param>
	/// <exception cref="UploadRefusedException">The form is malformed or over its bounds, or the file has the wrong content or too many bytes.</exception>
	public static async Task<UploadForm> ReadAsync(HttpContext context, string fileField, UploadSignature signature,
		long fileLimit, IReadOnlyCollection<string> fieldsBeforeFile, bool seekable, CancellationToken ct)
	{
		if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var type) ||
		    !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) ||
		    HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 } boundary)
			throw new UploadRefusedException(UploadRefusal.Malformed);

		var reader = new MultipartReader(boundary, context.Request.Body)
			{ HeadersCountLimit = 16, HeadersLengthLimit = 8 * 1024, BodyLengthLimit = null };
		var fields = new Dictionary<string, string>(StringComparer.Ordinal);
		Stream? file = null;
		string? fileName = null;
		try
		{
			while (await reader.ReadNextSectionAsync(ct) is { } section)
			{
				if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
				    !disposition.IsFormDisposition() && !disposition.IsFileDisposition())
					throw new UploadRefusedException(UploadRefusal.Malformed);

				var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? string.Empty;
				if (disposition.IsFileDisposition())
				{
					// Any other file is skipped unread.
					if (name != fileField || file is not null) continue;

					fileName = HeaderUtilities.RemoveQuotes(disposition.FileName).Value ?? string.Empty;
					file = new GuardedUploadStream(section.Body, fileLimit, signature);
					if (!seekable && fieldsBeforeFile.All(fields.ContainsKey)) break;

					file = await SpoolAsync(file, ct);
					continue;
				}

				if (fields.Count >= MaxFields) throw new UploadRefusedException(UploadRefusal.Malformed);

				fields[name] = await ReadTextAsync(section.Body, ct);
			}
		}
		catch
		{
			if (file is not null) await file.DisposeAsync();
			throw;
		}

		return new UploadForm(fields, fileName, file);
	}

	private static async Task<string> ReadTextAsync(Stream body, CancellationToken ct)
	{
		using var text = new StreamReader(body, Encoding.UTF8, false, 1024, true);
		var buffer = new char[MaxFieldLength + 1];
		var length = await text.ReadBlockAsync(buffer.AsMemory(), ct);
		return length > MaxFieldLength
			? throw new UploadRefusedException(UploadRefusal.Malformed)
			: new string(buffer, 0, length);
	}

	// A bounded temporary file, deleted when closed: only for callers that need a seekable file.
	private static async Task<Stream> SpoolAsync(Stream file, CancellationToken ct)
	{
		var spooled = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
			new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.ReadWrite,
				Share = FileShare.None,
				Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
			});
		try
		{
			await file.CopyToAsync(spooled, ct);
			spooled.Position = 0;
			return spooled;
		}
		catch
		{
			await spooled.DisposeAsync();
			throw;
		}
	}
}
