using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Basil.Protocol.Game.Stable.Models.Authentication;
using Basil.Protocol.Game.Stable.Wire.Packets;

namespace LoadHarness;

/// <summary>An osu! (stable) client: signs in on <c>c.</c>, then polls with an occasional chat message and status change.</summary>
internal sealed class OsuClient(Context context, Account account, int index)
{
	private const string Kind = "osu";

	public static string LoginBody(Account account, int index)
	{
		var hash = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(account.Password)));
		var path = Hex(index, "path");
		var uninstall = Hex(index, "uninstall");
		var disk = Hex(index, "disk");
		// version | utc offset | display city | path:adapters:adapters hash:uninstall:disk: | block non-friend DMs
		return $"{account.Name}\n{hash}\nb20250101|0|0|{path}:runningunderwine:{Hex(index, "adaptersHash")}:{uninstall}:{disk}:|0\n";
	}

	public async Task RunAsync(CancellationToken ct)
	{
		string? token = null;
		try
		{
			token = await SignInAsync(ct);
			if (token is null) return;

			for (var poll = 1; !ct.IsCancellationRequested; poll++)
			{
				await Context.Pause(context.Options.OsuPoll, ct);
				await PollAsync(token, Body(poll), ct);
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			if (token is not null) await SignOutAsync(token);
		}
	}

	private async Task<string?> SignInAsync(CancellationToken ct)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, context.Url("c", "/"));
			request.Headers.UserAgent.ParseAdd("osu!");
			request.Content = new StringContent(LoginBody(account, index), new UTF8Encoding(false));
			using var response = await context.Http.SendAsync(request, ct);
			var body = await response.Content.ReadAsByteArrayAsync(ct);
			var elapsed = Stopwatch.GetElapsedTime(started);
			var token = response.Headers.TryGetValues("cho-token", out var values) ? values.FirstOrDefault() : null;
			// The first packet answers the login; its id is not positive when the sign-in was refused.
			var accepted = response.IsSuccessStatusCode && !string.IsNullOrEmpty(token) && token != "no" && body.Length >= 11
			               && BitConverter.ToUInt16(body) == (ushort)ServerPacketType.UserId
			               && BitConverter.ToInt32(body, 7) > 0;
			context.Recorder.Record("osu-login", elapsed, accepted ? null : $"login refused ({(int)response.StatusCode})",
				true);
			return accepted ? token : null;
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			context.Recorder.Record("osu-login", Stopwatch.GetElapsedTime(started), Context.Reason(exception), true);
			return null;
		}
	}

	private async Task PollAsync(string token, byte[] body, CancellationToken ct)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, context.Url("c", "/"));
			request.Headers.UserAgent.ParseAdd("osu!");
			request.Headers.Add("osu-token", token);
			request.Content = new ByteArrayContent(body);
			using var response = await context.Http.SendAsync(request, ct);
			await response.Content.ReadAsByteArrayAsync(ct);
			context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(started),
				response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(started), Context.Reason(exception));
		}
	}

	private async Task SignOutAsync(string token)
	{
		try
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			using var request = new HttpRequestMessage(HttpMethod.Post, context.Url("c", "/"));
			request.Headers.Add("osu-token", token);
			request.Content = new ByteArrayContent(Packet(ClientPacketType.Logout, w => w.Write(0)));
			(await context.Http.SendAsync(request, timeout.Token)).Dispose();
		}
		catch
		{
			// The session ends by its idle limit if this fails.
		}
	}

	// Every poll pings; now and then it also chats and changes status, as a player in the menu does.
	private byte[] Body(int poll)
	{
		var body = new MemoryStream();
		body.Write(Packet(ClientPacketType.Ping, _ => { }));
		if (poll % 20 == 0)
			body.Write(Packet(ClientPacketType.SendPublicMessage, w =>
			{
				OsuString(w, "");
				OsuString(w, $"load {account.Name} {poll}");
				OsuString(w, "#osu");
				w.Write(0);
			}));
		if (poll % 45 == 0)
			body.Write(Packet(ClientPacketType.ChangeAction, w =>
			{
				w.Write((byte)(poll / 45 % 2));
				OsuString(w, "");
				OsuString(w, "");
				w.Write(0u);
				w.Write((byte)0);
				w.Write(0);
			}));
		return body.ToArray();
	}

	private static byte[] Packet(ClientPacketType type, Action<BinaryWriter> payload)
	{
		using var body = new MemoryStream();
		using var writer = new BinaryWriter(body);
		payload(writer);
		writer.Flush();
		using var packet = new MemoryStream();
		using var header = new BinaryWriter(packet);
		header.Write((ushort)type);
		header.Write((byte)0);
		header.Write((int)body.Length);
		header.Write(body.ToArray());
		header.Flush();
		return packet.ToArray();
	}

	private static void OsuString(BinaryWriter writer, string value)
	{
		if (value.Length == 0)
		{
			writer.Write((byte)0);
			return;
		}

		var bytes = Encoding.UTF8.GetBytes(value);
		writer.Write((byte)0x0B);
		var length = (uint)bytes.Length;
		do
		{
			var next = (byte)(length & 0x7F);
			length >>= 7;
			writer.Write((byte)(length != 0 ? next | 0x80 : next));
		} while (length != 0);

		writer.Write(bytes);
	}

	private static string Hex(int index, string part)
	{
		return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes($"load-{index}-{part}")));
	}
}
