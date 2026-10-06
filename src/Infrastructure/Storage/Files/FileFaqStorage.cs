using Basil.Application.Storage.Contracts.Content;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Stores FAQ entries as <c>.txt</c> files, nested by the <c>:</c> segments of entry names.</summary>
internal sealed class FileFaqStorage(DataPaths paths) : IFaqStorage
{
	/// <inheritdoc />
	public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(paths.Faqs);

		return Task.FromResult<IReadOnlyList<string>>([.. Directory
			.EnumerateFiles(paths.Faqs, "*.txt", SearchOption.AllDirectories)
			.Select(file =>
			{
				var relative = Path.GetRelativePath(paths.Faqs, file);
				return Path.ChangeExtension(relative, null)
					.Replace(Path.DirectorySeparatorChar, ':')
					.Replace(Path.AltDirectorySeparatorChar, ':');
			})
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)]);
	}

	/// <inheritdoc />
	public Task SaveAsync(string entry, Stream content, CancellationToken cancellationToken = default)
	{
		return FileStorage.SaveAsync(PathFor(paths.Faqs, entry), content, cancellationToken);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(string entry, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<Stream?>(FileStorage.Open(PathFor(paths.Faqs, entry)));
	}

	/// <inheritdoc />
	public Task DeleteAsync(string entry, CancellationToken cancellationToken = default)
	{
		FileStorage.Delete(PathFor(paths.Faqs, entry));
		return Task.CompletedTask;
	}

	/// <summary>Resolves the path of a FAQ entry's file.</summary>
	/// <param name="root">The directory the entry files belong to.</param>
	/// <param name="entry">The name of the entry; its <c>:</c> segments name the subdirectories.</param>
	/// <returns>The absolute path of the entry's <c>.txt</c> file.</returns>
	/// <exception cref="ArgumentException">The <paramref name="entry" /> names an unsafe path.</exception>
	private static string PathFor(string root, string entry)
	{
		return SafePath.Combine(root, string.Join('/', entry.Split(':')) + ".txt");
	}
}
