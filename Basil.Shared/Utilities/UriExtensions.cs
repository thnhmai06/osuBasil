namespace Basil.Shared.Utilities;

public static class UriExtensions
{
	extension(Uri value)
	{
		public bool IsHttp()
		{
			return value.IsAbsoluteUri && (value.Scheme == Uri.UriSchemeHttp || value.Scheme == Uri.UriSchemeHttps);
		}
	}
}