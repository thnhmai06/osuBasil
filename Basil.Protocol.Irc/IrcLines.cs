using System.Runtime.CompilerServices;
using System.Text;

namespace Basil.Protocol.Irc;

/// <summary>Reads IRC lines from a byte stream.</summary>
public static class IrcLines
{
	/// <summary>Reads the lines a stream carries, each ended by a line feed with an optional carriage return before it.</summary>
	/// <param name="stream">The stream to read.</param>
	/// <param name="ct">Stops the reading.</param>
	/// <returns>The lines without their terminator, in order; a line longer than <see cref="IrcWire.MaxLineBytes"/> is returned as null. The sequence ends when the stream does.</returns>
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
					if (pending.Count >= IrcWire.MaxLineBytes)
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

	/// <summary>The bytes of a line without its message tags, which is what the body limit counts.</summary>
	public static int BodyBytes(string line)
	{
		var body = line.StartsWith('@') && line.IndexOf(' ') is var space and >= 0 ? line[(space + 1)..] : line;
		return Encoding.UTF8.GetByteCount(body);
	}
}
