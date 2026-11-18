using System.Net;
using Basil.Domain.Authentication;
using Basil.Domain.Game.Client;

using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Tests.Authentication;

public class SessionRecordTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void SameId_TrueWhateverData()
	{
		var id = 7L;
		var first = Make(id);
		var later = Make(id, lastActiveAt: Start.AddHours(1), tokenHash: new Sha256Hash("abc"u8.ToArray()));

		Assert.Equal(first, later);
		Assert.Equal(first.GetHashCode(), later.GetHashCode());
	}

	[Fact]
	public void DifferentId_FalseWhateverData()
	{
		Assert.NotEqual(Make(1), Make(2));
	}

	[Fact]
	public void NonPositiveId_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(0));
	}

	[Fact]
	public void UndefinedType_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(type: 0));
	}

	[Theory]
	[InlineData(SessionType.Stable)]
	[InlineData(SessionType.StableTourney)]
	public void OsuNoClient_Throws(SessionType type)
	{
		Assert.Throws<ArgumentException>(() => Make(type: type, client: null));
	}

	[Theory]
	[InlineData(SessionType.Stable)]
	[InlineData(SessionType.StableTourney)]
	public void OsuWithClient_Accepted(SessionType type)
	{
		Assert.Equal(type, Make(type: type, client: Client()).Value.Type);
	}

	[Theory]
	[InlineData(SessionType.Irc)]
	[InlineData(SessionType.Api)]
	public void NonOsu_NoClientOk(SessionType type)
	{
		Assert.Equal(type, Make(type: type, client: null).Value.Type);
	}

	[Fact]
	public void ActiveBeforeStart_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(lastActiveAt: Start.AddTicks(-1)));
	}

	[Fact]
	public void ActiveAtStart_Accepted()
	{
		Assert.Equal(Start, Make(lastActiveAt: Start).Value.LastActiveAt);
	}

	private static SessionRecord Make(long? id = null, SessionType type = SessionType.Api, OsuClient? client = null,
		DateTimeOffset? lastActiveAt = null, Sha256Hash? tokenHash = null)
	{
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "session_user" } };
		return new SessionRecord
		{
			Id = id ?? 1,
			Value = new SessionData
			{
				User = user,
				Type = type,
				StartedAt = Start,
				Fingerprint = new SessionFingerprint(IPAddress.Loopback, client),
				TokenHash = tokenHash ?? new Sha256Hash("token"u8.ToArray()),
				ExpiresAt = null,
				LastActiveAt = lastActiveAt ?? Start
			}
		};
	}

	private static OsuClient Client()
	{
		return new OsuClient(new OsuVersion(new DateOnly(2026, 1, 2), null, OsuVersionStream.Stable),
			new OsuFingerprint(new Md5Hash("0cc175b9c0f1b6a831c399e269772661"),
				new Md5Hash("4a8a08f09d37b73795649038408b5f33"), new Md5Hash("f97c5d29941bfb1b2fdab0874906ab82"),
				new NetworkAdapters
				{
					Adapters = "adapter-one.", Hash = new Md5Hash("92eb5ffee6ae2fec3ad71c777531578f")
				}));
	}
}