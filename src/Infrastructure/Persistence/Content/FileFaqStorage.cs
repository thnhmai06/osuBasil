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
	public Task<IFaqEntryWriter> CreateOrUpdateAsync(string entry, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IFaqEntryWriter>(EntryWriter.Create(Files.Resolve(FileNameOf(entry))));
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
		ArgumentNullException.ThrowIfNull(entry);
		var parts = entry.Split(':');
		if (parts.Any(string.IsNullOrWhiteSpace))
			throw new ArgumentException("An entry name and each part of it must name something.", nameof(entry));

		return string.Join('/', parts) + ".txt";
	}

	/// <summary>Writes an entry's new text to a temporary file and puts it in place once committed.</summary>
	private sealed class EntryWriter : StreamWriter, IFaqEntryWriter
	{
		private readonly string _path;
		private readonly string _temporary;
		private bool _committed;

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

		/// <summary>Stores the text written so far, replacing the entry's previous text; committing again does nothing.</summary>
		public void Commit()
		{
			if (_committed) return;
			Flush();
			BaseStream.Dispose();
			File.Move(_temporary, _path, true);
			_committed = true;
		}

		Task IFaqEntryWriter.WriteAsync(string text)
		{
			return WriteAsync(text);
		}

		public static EntryWriter Create(string path)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			return new EntryWriter(path, TempFiles.Name(path));
		}

		protected override void Dispose(bool disposing)
		{
			if (!disposing || !_committed) base.Dispose(disposing);
			if (!disposing || _committed) return;
			if (File.Exists(_temporary)) File.Delete(_temporary);
		}
	}
}