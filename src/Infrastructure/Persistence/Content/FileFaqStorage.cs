using Basil.Application.Persistence.Content;
using Basil.Infrastructure.Persistence.Files;

namespace Basil.Infrastructure.Persistence.Content;

/// <summary>Stores FAQ entries as files in the data directory.</summary>
internal sealed class FileFaqStorage(DataPaths paths) : IFaqStorage
{
	private DataStorage Files => paths.Faqs;

	/// <inheritdoc />
	public Task<IEnumerable<string>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(
			Files.Keys.Where(key => key.EndsWith(".txt", StringComparison.Ordinal))
				.Order(StringComparer.OrdinalIgnoreCase)
				.Select(key => key[..^4].Replace('/', ':')));
	}

	/// <inheritdoc />
	public Task<StreamWriter> CreateOrUpdateAsync(string entry, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<StreamWriter>(EntryWriter.Create(Files.Resolve(FileNameOf(entry))));
	}

	/// <inheritdoc />
	public Task<StreamReader?> ReadAsync(string entry, CancellationToken cancellationToken = default)
	{
		var fileName = FileNameOf(entry);
		Files.Resolve(fileName); // an unsafe entry name is refused, not reported as missing
		return Task.FromResult(Files.Open(fileName) is { } stream ? new StreamReader(stream) : null);
	}

	/// <inheritdoc />
	public Task DeleteAsync(string entry, CancellationToken cancellationToken = default)
	{
		var fileName = FileNameOf(entry);
		Files.Resolve(fileName); // an unsafe entry name is refused, not ignored
		Files.Delete(fileName);
		return Task.CompletedTask;
	}

	/// <summary>Gets the name of an entry's file: its <c>:</c> segments name the folders.</summary>
	private static string FileNameOf(string entry)
	{
		return string.Join('/', entry.Split(':')) + ".txt";
	}

	/// <summary>Writes an entry's new text to a temporary file and puts it in place once disposed.</summary>
	private sealed class EntryWriter : StreamWriter
	{
		private readonly string _path;
		private readonly string _temporary;

		private EntryWriter(string path, string temporary) : base(new FileStream(temporary, new FileStreamOptions
		{
			Mode = FileMode.CreateNew,
			Access = FileAccess.Write,
			Share = FileShare.None,
			Options = FileOptions.Asynchronous
		}))
		{
			_path = path;
			_temporary = temporary;
		}

		public static EntryWriter Create(string path)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			return new EntryWriter(path, path + "." + Path.GetRandomFileName() + ".tmp");
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (!disposing) return;
			try
			{
				File.Move(_temporary, _path, true);
			}
			finally
			{
				if (File.Exists(_temporary)) File.Delete(_temporary);
			}
		}
	}
}