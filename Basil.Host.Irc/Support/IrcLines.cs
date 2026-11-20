using System.Runtime.CompilerServices;
using System.Text;

namespace Basil.Host.Irc.Support;

internal static class IrcLines
{
	public static async IAsyncEnumerable<string?> ReadAsync(Stream stream,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var pending = new List<byte>();
		var overflow = false;
		var buffer = new byte[4096];
		while (true)
		{
			var read = await stream.ReadAsync(buffer, ct);
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