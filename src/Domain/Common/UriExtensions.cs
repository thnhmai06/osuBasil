using System.Runtime.CompilerServices;

namespace Basil.Domain.Common;

/// <summary>Rules that every address the server keeps follows.</summary>
public static class UriExtensions
{
	extension(Uri value)
	{
		/// <summary>Tells whether an address is absolute with the http or https scheme.</summary>
		public bool IsHttp()
		{
			return value.IsAbsoluteUri && (value.Scheme == Uri.UriSchemeHttp || value.Scheme == Uri.UriSchemeHttps);
		}

		/// <summary>Returns the address unchanged, or throws when it is not absolute.</summary>
		/// <exception cref="ArgumentException">The address is not absolute.</exception>
		public Uri ThrowIfNotAbsolute([CallerArgumentExpression(nameof(value))] string? paramName = null)
		{
			return value.IsAbsoluteUri
				? value
				: throw new ArgumentException("The address must be absolute.", paramName);
		}
	}
}