namespace Basil.Application.Content.Storage;

public interface IFaqEntryWriter : IAsyncDisposable, IDisposable
{
	Task WriteAsync(string text);

	void Commit();
}