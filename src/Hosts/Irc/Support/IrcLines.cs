using System.Runtime.CompilerServices;
using System.Text;

namespace Basil.Host.Irc.Support;

/// <summary>Reads lines from a socket without letting one client hold unbounded memory.</summary>
internal static class IrcLines
{
	/// <summary>Reads the lines of a stream until it ends.</summary>
	/// <param name="stream">The socket stream.</param>
	/// <param name="cancellationToken">A token that stops reading.</param>
	/// <returns>Each line without its terminator, or <see langword="null" /> for a line longer than the limit.</returns>
	public static async IAsyncEnumerable<string?> ReadAsync(Stream stream,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var pending = new List<byte>();
		var overflow = false;
		var buffer = new byte[4096];
		while (true)
		{
			var read = await stream.ReadAsync(buffer, cancellationToken);
			if (read == 0) yield break;

			for (var i = 0; i < read; i++)
			{
				var value = buffer[i];
				if (value == '\n')
				{
					if (pending.Count > 0 && pending[^1] == '\r') pending.RemoveAt(pending.Count - 1);
					yield return overflow ? null : Encoding.UTF8.GetString([.. pending]);
					pending.Clear();
					overflow = false;
				}
				else if (!overflow)
				{
					if (pending.Count >= IrcReplies.MaxLineBytes)
					{
						overflow = true;
						pending.Clear();
					}
					else
					{
						pending.Add(value);
					}
				}
			}
		}
	}
}
