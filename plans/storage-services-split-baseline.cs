// Behaviour baseline for plans/storage-services-split-plan-20261003.md (appendix A, section 13).
// Run outside the repository (Directory.Packages.props blocks #:package here): copy this file to an empty
// folder, fix the #:project path if needed, then `dotnet run check.cs`. Port its scenarios into the test
// projects when they are migrated.
#:project V:/Code/cs/osuBasil/src/Basil.Application.Services/Basil.Application.Services.csproj
#:package Microsoft.Extensions.TimeProvider.Testing@9.9.0
#:package Microsoft.Extensions.DependencyInjection@10.0.0
using System.Net;
using Basil.Application.Services;
using Basil.Application.Storage.Common;
using Basil.Application.Storage.Beatmaps;
using Basil.Application.Storage.Chat;
using Basil.Application.Contracts.Anticheat;
using Basil.Application.Contracts.Beatmaps;
using Basil.Application.Contracts.Scores;
using Basil.Application.Storage.Scores;
using System.Text;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Application.Contracts.Chat;
using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Sessions;
using Basil.Application.Contracts.Users;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Sessions;
using Basil.Application.Storage.Users;
using Basil.Domain.Auth;
using Basil.Domain.Chat;
using Basil.Domain.Client;
using Basil.Domain.Social;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Domain.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

var failures = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}"); if (!ok) failures++; }
List<T> Drain<T>(System.Threading.Channels.ChannelReader<T> r) { var l = new List<T>(); while (r.TryRead(out var e)) l.Add(e); return l; }

var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-01T00:00:00Z"));
var userStore = new Users();
var credentialStore = new Credentials();
var loginStore = new Logins();
var services = new ServiceCollection();
services.AddSingleton<TimeProvider>(time);
services.AddSingleton<IUserRepository>(userStore);
services.AddSingleton<ICredentialRepository>(credentialStore);
services.AddSingleton<ILoginRepository>(loginStore);
var matchStore = new Matches();
services.AddSingleton<IMatchRepository>(matchStore);
var scoreStore = new ScoresFake(); services.AddSingleton<IScoreRepository>(scoreStore);
var replayStore = new Replays(); services.AddSingleton<IReplayStorage>(replayStore);
services.AddSingleton<IUserStatsRepository>(new StatsFake());
var archiveReader = new Reader(); services.AddSingleton<IBeatmapsetReader>(archiveReader);
services.AddSingleton<IBeatmapAnalyser>(new Analyser());
var setStore = new Sets(); services.AddSingleton<IBeatmapsetRepository>(setStore);
var mapStore = new Maps(); services.AddSingleton<IBeatmapRepository>(mapStore);
var archiveStore = new Archives(); services.AddSingleton<IBeatmapsetStorage>(archiveStore);
var roundStore = new Rounds(); services.AddSingleton<IRoundRepository>(roundStore);
var eventStore = new MatchEvents(); services.AddSingleton<IMatchEventRepository>(eventStore);
var relationStore = new Relationships();
services.AddSingleton<IRelationshipRepository>(relationStore);
services.AddApplicationServices();
var provider = services.BuildServiceProvider();
var auth = provider.GetRequiredService<IAuthService>();
var sessions = provider.GetRequiredService<ISessionService>();
var userService = provider.GetRequiredService<IUserService>();
var users = provider.GetRequiredService<UserRegistry>();
var lobby = provider.GetRequiredService<Lobby>();
var lobbyService = provider.GetRequiredService<ILobbyService>();
var roomService = provider.GetRequiredService<IRoomService>();
var client = new ClientInfo(new ClientVersion(new DateOnly(2025, 1, 1), null, ClientVersionStream.Stable),
	new ClientFingerprint(new Md5(new byte[16]), new NetworkAdapters("adapter.", new Md5(new byte[16])), new Md5(new byte[16]), new Md5(new byte[16])));
var password = new Md5(new byte[16]);

var nextUser = 1;
User NewUser(string name, ClientPrivileges? privilege = null)
{
	var user = new User { Id = nextUser++, Value = new UserData { Name = name } };
	if (privilege is { } p) user.Value.Privilege = p;
	userStore.Put(user);
	return user;
}
async Task<LoginResult> LoginAs(User u, ConnectionType type = ConnectionType.Bancho) =>
	await auth.LoginAsync(new LoginAttempt(u.Value.Name, password), type, IPAddress.Loopback, type is ConnectionType.Irc ? null : client, 0);
BanchoConnection Online(User u) { var r = LoginAs(u).GetAwaiter().GetResult(); Check($"login {u.Value.Name}", r.Succeeded); return (BanchoConnection)r.Connection!; }
Task<RoomResult> Do(Room room, Func<IRoomService, Task<RoomResult>> op) => op(roomService);

// S1: tournament room opened empty: announced at 15 min left, again at 5 min left, closed at 15 min
var referee = NewUser("Referee");
var opened = time.GetUtcNow();
var (t1, r1) = await lobbyService.OpenAsync(referee, null, "T1", "", isTournament: true, isPrivate: false);
var ev = Drain(lobbyService.Events);
Check("S1 opening carries the closing time (15 min)", r1 == RoomResult.Ok && ev.OfType<LobbyRoomOpened>().SingleOrDefault()?.ClosesAt == opened + TimeSpan.FromMinutes(15) && !ev.OfType<LobbyRoomClosingAnnounced>().Any());
time.Advance(TimeSpan.FromMinutes(9));
Check("S1 nothing before 10 min", Drain(lobbyService.Events).Count == 0);
time.Advance(TimeSpan.FromMinutes(1));
ev = Drain(lobbyService.Events);
Check("S1 announced at 5 min left", ev.OfType<LobbyRoomClosingAnnounced>().SingleOrDefault()?.ClosesAt == opened + TimeSpan.FromMinutes(15) && lobby.Find(t1!.Id) is not null);
time.Advance(TimeSpan.FromMinutes(5));
ev = Drain(lobbyService.Events);
Check("S1 closed at 15 min", ev.OfType<LobbyRoomClosed>().Count() == 1 && lobby.Find(t1.Id) is null);

// S2: a join after the second warning cancels the close; emptying again restarts at 15 min
var (t2, _) = await lobbyService.OpenAsync(referee, null, "T2", "", true, false);
var alice = NewUser("Alice"); var aliceConn = Online(alice);
time.Advance(TimeSpan.FromMinutes(11));
Drain(lobbyService.Events);
Check("S2 alice joins", await Do(t2!, s => s.Members.JoinAsync(t2!, aliceConn, "")) == RoomResult.Ok);
time.Advance(TimeSpan.FromMinutes(10));
Check("S2 room still open, no announcement", lobby.Find(t2.Id) is not null && Drain(lobbyService.Events).Count == 0);
var left = time.GetUtcNow();
await Do(t2, s => s.Members.LeaveAsync(t2!, aliceConn));
ev = Drain(lobbyService.Events);
Check("S2 emptied again: announced at 15 min left", ev.OfType<LobbyRoomClosingAnnounced>().SingleOrDefault()?.ClosesAt == left + TimeSpan.FromMinutes(15));
time.Advance(TimeSpan.FromMinutes(15));
Check("S2 closed 15 min after emptying", lobby.Find(t2.Id) is null);

// S3: normal room closes as soon as its last player leaves
var bob = NewUser("Bob"); var bobConn = Online(bob);
var (n1, rn) = await lobbyService.OpenAsync(bob, bobConn, "N1", "", false, false);
Check("S3 creator seated as host", rn == RoomResult.Ok && ReferenceEquals(n1!.Host, bobConn));
Check("S3 open in game while playing elsewhere refused", (await lobbyService.OpenAsync(bob, bobConn, "N2", "", false, false)).Result == RoomResult.AlreadyInRoom);
await Do(n1, s => s.Members.LeaveAsync(n1!, bobConn));
Check("S3 closed when empty", lobby.Find(n1.Id) is null);

// S4: a closed seat is only replaced after every check passes
var carol = NewUser("Carol"); var carol1 = Online(carol);
var (t4, _) = await lobbyService.OpenAsync(referee, null, "T4", "pw", true, false);
Check("S4 carol joins", await Do(t4!, s => s.Members.JoinAsync(t4!, carol1, "pw")) == RoomResult.Ok);
time.Advance(TimeSpan.FromSeconds(11));
var carol2 = Online(carol); // replaces carol1 (idle); carol1 is now closed but still seated
Check("S4 old connection closed", !carol1.IsOpen);
Check("S4 wrong password refused", await Do(t4, s => s.Members.JoinAsync(t4!, carol2, "nope")) == RoomResult.WrongPassword);
Check("S4 old seat kept after refusal", t4.Slots.Find(carol1) is not null);
Check("S4 right password replaces seat", await Do(t4, s => s.Members.JoinAsync(t4!, carol2, "pw")) == RoomResult.Ok && t4.Slots.Find(carol1) is null && t4.Slots.Find(carol2) is not null);

// S5: a player leaving mid-load completes the "all loaded" set; last completion ends the round
var dave = NewUser("Dave"); var daveConn = Online(dave);
var erin = NewUser("Erin"); var erinConn = Online(erin);
var (t5, _) = await lobbyService.OpenAsync(referee, null, "T5", "", true, false);
var refConn = Online(referee);
await Do(t5!, s => s.Members.JoinAsync(t5!, daveConn, "")); await Do(t5, s => s.Members.JoinAsync(t5!, erinConn, ""));
Check("S5 configure map", await Do(t5, s => s.Settings.ConfigureAsync(t5!, refConn, new RoomSettingsChange(Beatmap: new BeatmapReference(new Md5(new byte[16]), 1, "map", GameMode.Standard, null)))) == RoomResult.Ok);
Drain(roomService.Events);
Check("S5 empty configure is no-op", await Do(t5, s => s.Settings.ConfigureAsync(t5!, refConn, new RoomSettingsChange())) == RoomResult.Ok && Drain(roomService.Events).Count == 0);
Check("S5 start", await Do(t5, s => s.Rounds.StartAsync(t5!, refConn)) == RoomResult.Ok);
await Do(t5, s => s.Rounds.MarkLoadedAsync(t5!, daveConn));
Drain(roomService.Events);
await Do(t5, s => s.Members.LeaveAsync(t5!, erinConn));
var re = Drain(roomService.Events);
Check("S5 all loaded reported in the leave event", re.OfType<RoomPlayerLeft>().Single().RoundProgress is { AllLoaded: true, Completed: false } && !re.OfType<RoomRoundAllLoaded>().Any());
await Do(t5, s => s.Rounds.CompleteAsync(t5!, daveConn));
re = Drain(roomService.Events);
Check("S5 round completed", re.OfType<RoomRoundCompleted>().Count() == 1 && !t5.InProgress);

// S6: a round nobody plays ends at once
await Do(t5, s => s.Slots.SetHasMapAsync(t5!, daveConn, false));
Drain(roomService.Events);
await Do(t5, s => s.Rounds.StartAsync(t5!, refConn));
re = Drain(roomService.Events);
Check("S6 unplayed round ends in its start event", re.OfType<RoomRoundStarted>().Single().Round.EndedAt is not null && !re.OfType<RoomRoundCompleted>().Any() && !t5.InProgress);

// S7: clearing the beatmap, then start is refused
Check("S7 clear beatmap", await Do(t5, s => s.Settings.ConfigureAsync(t5!, refConn, new RoomSettingsChange(ClearBeatmap: true))) == RoomResult.Ok && t5.Beatmap is null);
Check("S7 start without map refused", await Do(t5, s => s.Rounds.StartAsync(t5!, refConn)) == RoomResult.NoBeatmap);

// P2a: a logout within one second of login is ignored; a later one closes the connection
var fay = NewUser("Fay"); var fayConn = Online(fay);
sessions.Close(fayConn, ConnectionCloseReason.LoggedOut);
Check("P2a early logout ignored", fayConn.IsOpen);
time.Advance(TimeSpan.FromSeconds(2));
sessions.Close(fayConn, ConnectionCloseReason.LoggedOut);
Check("P2a later logout closes and takes the user offline", !fayConn.IsOpen && users.Find(fay) is null);

// P2b: an osu!tourney login needs the Player and Supporter privileges
var gus = NewUser("Gus", ClientPrivileges.Player);
Check("P2b tourney without supporter refused", (await LoginAs(gus, ConnectionType.Tourney)).Failure == LoginFailure.NoTourneyPermission);
Check("P2b tourney with supporter accepted", (await LoginAs(alice, ConnectionType.Tourney)).Succeeded);

// P2c: a second osu! login while the first is active is refused
var nora = NewUser("Nora"); Online(nora);
Check("P2c active connection not replaced", (await LoginAs(nora)).Failure == LoginFailure.AlreadyOnline);

// P2d: BasilBot comes online, is created when missing, and never logs in from a client
var botConn = await sessions.OpenBotAsync();
Check("P2d bot user created as id 0", botConn.User.Id == SystemUserIds.BasilBot && (await userStore.GetAsync(SystemUserIds.BasilBot)) is not null);
Check("P2d second call returns the open bot", ReferenceEquals(await sessions.OpenBotAsync(), botConn));
Check("P2d bot client login refused", (await LoginAs(botConn.User)).Failure == LoginFailure.WrongPassword);
Check("P2d bot cannot be silenced or deleted", !await userService.SilenceAsync(botConn.User, time.GetUtcNow().AddHours(1)) && !await userService.DeleteAsync(botConn.User));

// P2e: registration and the administrator key
Check("P2e no key: registration accepted", (await auth.RegisterAsync(new RegisterAttempt("Henry", password, null))).Failure is null);
credentialStore.AdminKey = new Md5(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
Check("P2e key set: missing key refused", await auth.CheckRegistrationAsync(new RegisterAttempt("Ivy", password, null)) == RegistrationFailure.WrongAdminKey);
Check("P2e key set: wrong key refused", await auth.CheckRegistrationAsync(new RegisterAttempt("Ivy", password, password)) == RegistrationFailure.WrongAdminKey);
Check("P2e key set: right key accepted", (await auth.RegisterAsync(new RegisterAttempt("Ivy", password, credentialStore.AdminKey))).User is not null);
Check("P2e name taken", await auth.CheckRegistrationAsync(new RegisterAttempt("ivy", password, credentialStore.AdminKey)) == RegistrationFailure.NameTaken);
Check("P2e invalid name", await auth.CheckRegistrationAsync(new RegisterAttempt("x", password, credentialStore.AdminKey)) == RegistrationFailure.InvalidName);
Check("P2e admin creates an account without a key", (await auth.CreateAccountAsync(new UserData { Name = "Jack" }, password)).User is not null);

// P2f: idle connections are closed after 300 s; activity keeps a connection; the bot is never closed
var kim = NewUser("Kim"); var kimConn = Online(kim);
var lee = NewUser("Lee"); var leeConn = Online(lee);
time.Advance(TimeSpan.FromSeconds(200));
sessions.MarkActive(leeConn);
time.Advance(TimeSpan.FromSeconds(150));
sessions.CloseIdle();
Check("P2f idle connection closed", !kimConn.IsOpen);
Check("P2f active connection kept", leeConn.IsOpen);
Check("P2f bot kept", botConn.IsOpen);

// P2g: silencing reaches the online session; deleting closes the user's connections
var mia = NewUser("Mia"); var miaConn = Online(mia);
var miaCopy = new User { Id = mia.Id, Value = new UserData { Name = "Mia" } };
var until = time.GetUtcNow().AddHours(1);
await userService.SilenceAsync(miaCopy, until);
Check("P2g silence applied to the online session", miaConn.User.Value.SilenceEndsAt == until);
Drain(sessions.Events);
await userService.DeleteAsync(miaCopy);
Check("P2g delete closes connections", !miaConn.IsOpen && Drain(sessions.Events).OfType<UserConnectionClosed>().Any(e => e.Reason == ConnectionCloseReason.Deleted));
Check("P2g deleted user loses every privilege", miaConn.User.Value.Privilege == ClientPrivileges.None);

// P2h: logins are recorded; announcements reach online osu! clients
Check("P2h logins recorded", loginStore.Count > 0);
Drain(sessions.Events);
Check("P2h announce", sessions.Announce("hello") > 0 && Drain(sessions.Events).OfType<UserNotificationSent>().Count() == 1);


// P3: channels
var channelService = provider.GetRequiredService<IChannelService>();
Drain(channelService.Events);
var osuChannel = channelService.Open(new GeneralChannel { Name = "#osu", Topic = "osu", AutoJoin = true });
Check("P3a general channel opened and announced", osuChannel is not null && Drain(channelService.Events).OfType<ChannelOpened>().Count() == 1);
Check("P3a same name refused", channelService.Open(new GeneralChannel { Name = "#OSU", Topic = "x" }) is null);
var oli = NewUser("Oli"); var oliConn = Online(oli);
var pam = NewUser("Pam"); var pamConn = Online(pam);
channelService.JoinAutoChannels(oliConn);
Check("P3b auto channel joined", osuChannel!.Members.Contains(oliConn));
Check("P3c non-member cannot post", await channelService.PostAsync(osuChannel, pamConn, "hi") == ChannelPostResult.NotMember);
Check("P3c empty refused", await channelService.PostAsync(osuChannel, oliConn, "   ") == ChannelPostResult.Empty);
Drain(channelService.Events);
Check("P3c long message cut", await channelService.PostAsync(osuChannel, oliConn, new string('a', 2500)) == ChannelPostResult.Posted
	&& Drain(channelService.Events).OfType<ChannelMessagePosted>().Single() is { Truncated: true } posted && posted.Message.Content.Length == 2000);
sessions.SetAway(pamConn.Session, "brb");
Drain(channelService.Events);
Check("P3d private message posted", await channelService.PostAsync(pamConn.Session.PmChannel, oliConn, "hey") == ChannelPostResult.Posted);
var pmEvents = Drain(channelService.Events).OfType<ChannelMessagePosted>().ToList();
Check("P3d away reply carried in the post", pmEvents.Count == 1 && pmEvents[0].AwayReply?.Content == "brb");
await channelService.PostAsync(pamConn.Session.PmChannel, oliConn, "notice", notice: true);
Check("P3e notice gets no away reply", Drain(channelService.Events).OfType<ChannelMessagePosted>().Count() == 1);
relationStore.Items.Add(new Relationship { Actor = pam, Target = oli, Type = RelationshipType.Block });
Check("P3f blocked author refused", await channelService.PostAsync(pamConn.Session.PmChannel, oliConn, "x") == ChannelPostResult.Blocked);
await userService.SilenceAsync(pam, time.GetUtcNow().AddHours(1));
Check("P3f block wins over a silenced recipient", await channelService.PostAsync(pamConn.Session.PmChannel, oliConn, "x") == ChannelPostResult.Blocked);
await userService.SilenceAsync(pam, time.GetUtcNow());
relationStore.Items.Clear();
sessions.SetPmPrivate(pamConn.Session, true);
Check("P3g friends-only refuses strangers", await channelService.PostAsync(pamConn.Session.PmChannel, oliConn, "x") == ChannelPostResult.Blocked);
relationStore.Items.Add(new Relationship { Actor = pam, Target = oli, Type = RelationshipType.Friend });
Check("P3g friends-only accepts friends", await channelService.PostAsync(pamConn.Session.PmChannel, oliConn, "x") == ChannelPostResult.Posted);
Check("P3h bot passes friends-only", await channelService.PostAsync(pamConn.Session.PmChannel, botConn, "x") == ChannelPostResult.Posted);
Check("P3i spectate", channelService.Spectators.Spectate(pamConn, oliConn) == SpectateResult.Spectating && users.FindSpectating(oliConn) is not null);
Check("P3i spectate self refused", channelService.Spectators.Spectate(pamConn, pamConn) == SpectateResult.Self);
Check("P3i bot can spectate", channelService.Spectators.Spectate(pamConn, botConn) == SpectateResult.Spectating);
Check("P3i stop", channelService.Spectators.StopSpectating(oliConn) && users.FindSpectating(oliConn) is null);
time.Advance(TimeSpan.FromSeconds(2));
sessions.Close(pamConn, ConnectionCloseReason.LoggedOut);
Check("P3j host leaving closes its spectator channel", pamConn.SpectatorChannel.IsClosed && pamConn.SpectatorChannel.Members.Count == 0);

// P4: rooms through the services
var quinn = NewUser("Quinn"); var quinnConn = Online(quinn);
var (p4, _) = await lobbyService.OpenAsync(referee, null, "P4", "", true, false);
await roomService.Members.JoinAsync(p4!, quinnConn, "");
Check("P4a bot has server authority", await roomService.Settings.ConfigureAsync(p4!, botConn, new RoomSettingsChange(Beatmap: new BeatmapReference(new Md5(new byte[16]), 1, "map", GameMode.Standard, null))) == RoomResult.Ok);
Check("P4a stranger refused", await roomService.Settings.ConfigureAsync(p4!, oliConn, new RoomSettingsChange(Name: "x")) == RoomResult.NotAuthorized);
await roomService.Rounds.StartAsync(p4!, botConn);
Drain(lobbyService.Events);
Check("P4b bot closes the room", await lobbyService.CloseAsync(p4!, botConn) == RoomResult.Ok);
Check("P4b closing mid-round reports the aborted round", Drain(lobbyService.Events).OfType<LobbyRoomClosed>().Single() is { AbortedRound: { Aborted: true } } closed && closed.Evicted.Count == 1);
Check("P4c operations on a closed room report RoomClosed", await roomService.Members.JoinAsync(p4!, quinnConn, "") == RoomResult.RoomClosed);

// P4b: settings, seating, arranging, anticheat
var rex = NewUser("Rex"); var rexConn = Online(rex);
var sam = NewUser("Sam"); var samConn = Online(sam);
var (q1, _) = await lobbyService.OpenAsync(referee, null, "Q1", "pw", true, false);
var (q2, _) = await lobbyService.OpenAsync(referee, null, "Q2", "", true, false);
await roomService.Members.JoinAsync(q2!, rexConn, "");
Check("P4b seat stranger refused", await roomService.Members.SeatAsync(q1!, oliConn, rexConn) == RoomResult.NotAuthorized);
Check("P4b seat moves from another room, password not asked", await roomService.Members.SeatAsync(q1!, botConn, rexConn) == RoomResult.Ok && q1!.Slots.Find(rexConn) is not null && q2!.Slots.Find(rexConn) is null);
Check("P4b seat twice refused", await roomService.Members.SeatAsync(q1!, botConn, rexConn) == RoomResult.AlreadySeated);
Check("P4b bot cannot be banned", await roomService.Members.BanAsync(q1!, botConn, botConn.User) == RoomResult.NotAuthorized);
Check("P4b bot cannot be made referee", await roomService.Authority.AddRefereeAsync(q1!, botConn, botConn.User) == RoomResult.NotAuthorized);
await roomService.Members.BanAsync(q1!, botConn, sam);
Check("P4b seat banned refused", await roomService.Members.SeatAsync(q1!, botConn, samConn) == RoomResult.Banned);
await roomService.Members.UnbanAsync(q1!, botConn, sam);
await roomService.Members.SeatAsync(q1!, botConn, samConn);
await roomService.Settings.ConfigureAsync(q1!, botConn, new RoomSettingsChange(Mods: GameMods.Hidden | GameMods.HardRock));
Check("P4b mode change drops invalid mods", await roomService.Settings.ConfigureAsync(q1!, botConn, new RoomSettingsChange(Mode: GameMode.Mania)) == RoomResult.Ok && q1!.Mode == GameMode.Mania && q1.Mods.IsValid(GameMode.Mania));
await roomService.Authority.SetHostAsync(q1!, botConn, rexConn);
Check("P4b host cannot change privacy", await roomService.Settings.ConfigureAsync(q1!, rexConn, new RoomSettingsChange(IsPrivate: true)) == RoomResult.NotAuthorized && !q1!.Match.Value.IsPrivate);
Check("P4b manager changes privacy", await roomService.Settings.ConfigureAsync(q1!, botConn, new RoomSettingsChange(IsPrivate: true)) == RoomResult.Ok && q1!.Match.Value.IsPrivate);
Drain(roomService.Events);
Check("P4b arrange missing a player refused", await roomService.Slots.ArrangeSlotsAsync(q1!, botConn, [new SlotArrangement(5, rex, null, false)]) == RoomResult.InvalidSettings);
Check("P4b arrange locked player slot refused", await roomService.Slots.ArrangeSlotsAsync(q1!, botConn, [new SlotArrangement(5, rex, null, true), new SlotArrangement(6, sam, null, false)]) == RoomResult.InvalidSettings);
Check("P4b arrange duplicate slot refused", await roomService.Slots.ArrangeSlotsAsync(q1!, botConn, [new SlotArrangement(5, rex, null, false), new SlotArrangement(5, sam, null, false)]) == RoomResult.InvalidSettings);
Check("P4b arrange swaps and locks", await roomService.Slots.ArrangeSlotsAsync(q1!, botConn, [new SlotArrangement(1, sam, null, false), new SlotArrangement(2, rex, null, false), new SlotArrangement(3, null, null, true)]) == RoomResult.Ok
	&& q1!.Slots.Find(samConn)?.Index == 1 && q1.Slots.Find(rexConn)?.Index == 2 && q1.Slots.At(3)!.Locked && ReferenceEquals(q1.Host, rexConn)
	&& Drain(roomService.Events).OfType<RoomSlotsArranged>().Count() == 1);
var anticheat = provider.GetRequiredService<IAnticheatService>();
Check("AC clean flags emit nothing", anticheat.Report(rexConn, ClientFlags.Clean) == ClientFlags.Clean && Drain(anticheat.Events).Count == 0);
Check("AC cheat signs announced with the room", anticheat.Report(rexConn, ClientFlags.SpeedHackDetected | ClientFlags.SpinnerHack) == (ClientFlags.SpeedHackDetected | ClientFlags.SpinnerHack)
	&& Drain(anticheat.Events).OfType<AnticheatPlayerFlagged>().Single() is { Room: { } flaggedRoom, Signs: ClientFlags.SpeedHackDetected | ClientFlags.SpinnerHack } && ReferenceEquals(flaggedRoom, q1));
Check("AC flags outside a room carry no room", anticheat.Report(oliConn, ClientFlags.SpeedHackDetected) != ClientFlags.Clean && Drain(anticheat.Events).OfType<AnticheatPlayerFlagged>().Single().Room is null);
var tom = NewUser("Tom"); var tomConn = Online(tom);
var (q3, _) = await lobbyService.OpenAsync(sam, null, "Q3", "", true, false);
await roomService.Members.JoinAsync(q3!, tomConn, "");
Check("P4b seat from a room the caller does not manage refused", await roomService.Members.SeatAsync(q1!, refConn, tomConn) == RoomResult.InAnotherRoom && q3!.Slots.Find(tomConn) is not null);
await userService.SilenceAsync(tom, time.GetUtcNow().AddHours(1));
Check("P4b seat silenced refused", await roomService.Members.SeatAsync(q1!, botConn, tomConn) == RoomResult.Silenced && q3!.Slots.Find(tomConn) is not null);

// P5: scores, round results, beatmaps, unfinished matches
var zero = new Md5(new byte[16]);
var client2 = new ClientInfo(new ClientVersion(new DateOnly(2025, 1, 1), null, ClientVersionStream.Stable),
	new ClientFingerprint(zero, new NetworkAdapters("adapter.", zero), new Md5(Encoding.UTF8.GetBytes("u")), new Md5(Encoding.UTF8.GetBytes("d"))));
var uma = NewUser("Uma");
var umaConn = (BanchoConnection)(await auth.LoginAsync(new LoginAttempt("Uma", password), ConnectionType.Bancho, IPAddress.Loopback, client2, 0)).Connection!;
var mapHash = new Md5(Encoding.UTF8.GetBytes("map"));
var (r5, _) = await lobbyService.OpenAsync(NewUser("Vic"), null, "R5", "", true, false);
await roomService.Members.SeatAsync(r5!, botConn, umaConn);
await roomService.Settings.ConfigureAsync(r5!, botConn, new RoomSettingsChange(Beatmap: new BeatmapReference(mapHash, 1, "map", GameMode.Standard, null), TeamType: GameTeamType.TeamVs));
await roomService.Rounds.StartAsync(r5!, botConn);
Drain(roomService.Events);
var scoreService = provider.GetRequiredService<IScoreService>();
var stamp = time.GetUtcNow();
var fp = "client-hash-as-sent";
Submission Sub(int total, ClientFlags flags = ClientFlags.Clean)
{
	var data = new ScoreData(null, mapHash, GameMode.Standard, GameMods.NoMod, new HitCounts(100, 0, 0, 0, 0, 0), total, 100, Grade.S, true, false, stamp)
		{ Checksum = new Md5(Encoding.UTF8.GetBytes(total.ToString())) };
	var raw = $"chickenmcnuggets{100}o15{0}{0}smustard{0}{0}uu{mapHash}{100}{false}{uma.Value.Name}{total}S{0}Q{true}{0}20250101{stamp:yyMMddHHmmss}{fp}";
	return new Submission { Score = data, HashByClient = new Md5(Encoding.UTF8.GetBytes(raw)), ClientFlags = flags };
}
Task<ScoreRejection?> Submit(Submission s, byte[]? replay, Md5? played = null) =>
	scoreService.SubmitAsync(umaConn, s, null, new SubmittedClient(fp, client2.Fingerprint, "u", "d", "20250101", played ?? mapHash), replay);
var replaysBefore = replayStore.Saved;
Check("P5a score on the round's beatmap accepted, short replay not kept", await Submit(Sub(1000), new byte[10]) is null && replayStore.Saved == replaysBefore);
Check("P5a score carries round and team", scoreStore.Items[^1].Value is { Round: not null, Team: not null, UserId: not null });
Check("P5a submitted event with stats", Drain(scoreService.Events).OfType<ScoreSubmitted>().Single().Stats.PlayCount == 1);
Check("P5b replay of 24+ bytes kept", await Submit(Sub(2000), new byte[30]) is null && replayStore.Saved == replaysBefore + 1);
Check("P5c duplicate checksum refused", await Submit(Sub(2000), new byte[30]) == ScoreRejection.Duplicate);
Drain(scoreService.Events);
Check("P5d submitted score names its room; flags are left to the host", await Submit(Sub(3000, ClientFlags.SpeedHackDetected), null) is null
	&& Drain(scoreService.Events).OfType<ScoreSubmitted>().Single().Room == r5 && Drain(anticheat.Events).Count == 0);
Check("P5e tampered submission refused", await Submit(Sub(4000) with { HashByClient = zero }, null) == ScoreRejection.SubmissionHashMismatch);
Check("P5f unknown beatmap refused", await Submit(Sub(5000), null, zero) == ScoreRejection.UnknownBeatmap);

var round5 = r5!.LastRound!;
Score S(int id, int total, GameTeam? team) => new Score { Id = id, Value = new ScoreData(id, mapHash, GameMode.Standard, GameMods.NoMod, new HitCounts(100, 0, 0, 0, 0, 0), total, 100, Grade.S, true, false, stamp) { Team = team } };
Check("P5g no score, no result", RoundResult.Decide(round5, []) is null);
Check("P5g player win by margin", RoundResult.Decide(round5, [S(1, 1000, null), S(2, 900, null)]) == new RoundResult(null, 1, 100));
Check("P5g team win by summed margin", RoundResult.Decide(round5, [S(1, 500, GameTeam.Blue), S(2, 500, GameTeam.Blue), S(3, 900, GameTeam.Red)]) == new RoundResult(GameTeam.Blue, null, 100));
Check("P5g draw", RoundResult.Decide(round5, [S(1, 900, null), S(2, 900, null)]) == new RoundResult(null, null, 0));
Check("P5g single score wins unopposed", RoundResult.Decide(round5, [S(1, 900, GameTeam.Red)]) == new RoundResult(GameTeam.Red, 1, 0));

var beatmapService = provider.GetRequiredService<IBeatmapsetService>();
BeatmapsetArchive Archive(int? setId, params (string Content, int? Id)[] diffs) =>
	new(setId, "Artist", "Title", "Mapper", diffs.Select(d => new BeatmapArchiveDifficulty(d.Id, "v", GameMode.Standard, Encoding.UTF8.GetBytes(d.Content))).ToList());
Task<BeatmapsetImportResult> Import(BeatmapsetArchive? a, int? named = null) { archiveReader.Next = a; return beatmapService.ImportAsync(new MemoryStream([1]), named); }
Check("P5h unreadable archive", (await Import(null)).Failure == BeatmapsetImportFailure.Unreadable);
Check("P5h unknown named id falls back to the declared id", (await Import(Archive(123, ("a", 456)), 999)).Set?.Id == 123);
Check("P5h beatmap keeps its declared id", (await mapStore.GetByHashAsync(new Md5(Encoding.UTF8.GetBytes("a"))))?.Id == 456);
Check("P5h known beatmap picks its set and keeps its id", (await Import(Archive(777, ("a", null), ("b", null)))).Set?.Id == 123 && (await mapStore.GetByHashAsync(new Md5(Encoding.UTF8.GetBytes("a"))))?.Id == 456);
Check("P5h nothing declared gets a local id", (await Import(Archive(null, ("c", null)))).Set?.Id == 1_000_000_000);
Check("P5h existing named id wins over the declared id", (await Import(Archive(888, ("d", null)), 123)).Set?.Id == 123);
Check("P5h difficulties no longer in the set removed", await mapStore.GetByHashAsync(new Md5(Encoding.UTF8.GetBytes("a"))) is null);
var set123 = (await setStore.GetAsync(123))!;
set123.Locked = true;
Check("P5i locked set not imported", (await Import(Archive(123, ("e", null)))).Failure == BeatmapsetImportFailure.Locked);
Check("P5i locked set not deleted", !await beatmapService.DeleteAsync(set123));
set123.Locked = false;
Drain(beatmapService.Events);
Check("P5i delete", await beatmapService.DeleteAsync(set123) && await setStore.GetAsync(123) is null && Drain(beatmapService.Events).OfType<BeatmapsetDeleted>().Count() == 1);
archiveStore.Stored.Clear();
Check("P5j scan forgets sets without an archive", await beatmapService.ScanAsync() == 1 && await setStore.GetAsync(1_000_000_000) is null);

var matchService = provider.GetRequiredService<IMatchService>();
var unfinished = matchStore.Items.Count(m => m.Value.EndedAt is null);
roundStore.Items.Add(round5);
Check("P5k unfinished matches and rounds closed", unfinished > 0 && await matchService.CloseUnfinishedAsync() == unfinished
	&& matchStore.Items.All(m => m.Value.EndedAt is not null) && round5 is { Aborted: true, EndedAt: not null }
	&& eventStore.Items.Count(e => e.Type == MatchEventType.Closed) == unfinished);

// R: review fixes
var (rc, _) = await lobbyService.OpenAsync(NewUser("Wes"), null, "RC", "", true, false);
await roomService.Settings.ConfigureAsync(rc!, botConn, new RoomSettingsChange(Beatmap: new BeatmapReference(mapHash, 1, "map", GameMode.Standard, null)));
Drain(roomService.Events);
await roomService.Rounds.StartCountdownAsync(rc!, botConn, TimeSpan.FromSeconds(130), true);
for (var i = 0; i < 131; i++) time.Advance(TimeSpan.FromSeconds(1));
var ce = Drain(roomService.Events);
Check("R countdown marks for a round start", ce.OfType<RoomCountdownTicked>().Select(t => (int)t.Remaining.TotalSeconds).SequenceEqual([120, 60, 30, 10, 5, 4, 3]));
Check("R countdown starts the round in one event", ce.OfType<RoomRoundStarted>().SingleOrDefault()?.ByCountdown == true && !ce.OfType<RoomCountdownElapsed>().Any());
await roomService.Rounds.StartCountdownAsync(rc!, botConn, TimeSpan.FromSeconds(70), false);
for (var i = 0; i < 71; i++) time.Advance(TimeSpan.FromSeconds(1));
ce = Drain(roomService.Events);
Check("R timer marks", ce.OfType<RoomCountdownTicked>().Select(t => (int)t.Remaining.TotalSeconds).SequenceEqual([60, 30, 10, 5]) && ce.OfType<RoomCountdownElapsed>().Count() == 1);
await roomService.Rounds.StartCountdownAsync(rc!, botConn, TimeSpan.FromSeconds(60), true);
Drain(roomService.Events);
Check("R gameplay setting cancels auto-start", await roomService.Settings.ConfigureAsync(rc!, botConn, new RoomSettingsChange(Mods: GameMods.Hidden)) == RoomResult.Ok
	&& Drain(roomService.Events).OfType<RoomSettingsChanged>().Single().CountdownCancelled && rc!.CountdownEndsAt is null);
await roomService.Rounds.StartCountdownAsync(rc!, botConn, TimeSpan.FromSeconds(60), true);
Drain(roomService.Events);
Check("R room name keeps auto-start", await roomService.Settings.ConfigureAsync(rc!, botConn, new RoomSettingsChange(Name: "renamed")) == RoomResult.Ok
	&& !Drain(roomService.Events).OfType<RoomSettingsChanged>().Single().CountdownCancelled && rc!.CountdownEndsAt is not null && rc.Channel.Channel.Topic == "renamed");
Check("R analysis failure stores a zero star rating", (await Import(Archive(4242, ("fail", null)))).Beatmaps.Single().Difficulty.Star == 0);
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");

sealed class Matches : IMatchRepository
{
	private int _next = 1;
	public List<Match> Items { get; } = [];
	public Task<Match> CreateAsync(MatchData data, CancellationToken cancellationToken = default) { var m = new Match { Id = _next++, Value = data }; Items.Add(m); return Task.FromResult(m); }
	public Task CreateOrUpdateAsync(Match match, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public ValueTask<Match?> GetAsync(int id, CancellationToken cancellationToken = default) => ValueTask.FromResult<Match?>(null);
	public Task<Page<Match>> ListAsync(MatchQuery query, PageRequest page, CancellationToken cancellationToken = default) =>
		Task.FromResult(new Page<Match>(Items.Where(m => query.Ended is null || (m.Value.EndedAt is not null) == query.Ended).Skip(page.Offset).Take(page.Limit).ToList(), Items.Count));
}

sealed class Users : IUserRepository
{
	private readonly Dictionary<int, User> _byId = new();
	private int _next = 1000;
	public void Put(User user) => _byId[user.Id] = user;
	public Task<User> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		var user = new User { Id = _next++, Value = data };
		_byId[user.Id] = user;
		return Task.FromResult(user);
	}
	public Task CreateOrUpdateAsync(User user, CancellationToken cancellationToken = default) { _byId[user.Id] = user; return Task.CompletedTask; }
	public ValueTask<User?> GetAsync(int id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_byId.GetValueOrDefault(id));
	public ValueTask<User?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(_byId.Values.FirstOrDefault(u => string.Equals(u.Value.Name.Replace(' ', '_'), name.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)));
	public Task<Page<User>> ListAsync(UserQuery query, PageRequest page, CancellationToken cancellationToken = default) =>
		Task.FromResult(new Page<User>(_byId.Values.ToList(), _byId.Count));
}

sealed class Credentials : ICredentialRepository
{
	public Md5? AdminKey { get; set; }
	public Task<bool> VerifyAsync(Basil.Domain.Auth.Credentials credentials, CancellationToken cancellationToken = default) => Task.FromResult(true);
	public Task CreateOrUpdateAsync(Basil.Domain.Auth.Credentials credentials, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public Task<bool> VerifyAdminKeyAsync(Md5 key, CancellationToken cancellationToken = default) => Task.FromResult(AdminKey is { } k && k == key);
	public ValueTask<DateTimeOffset?> GetAdminKeyUpdatedAtAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<DateTimeOffset?>(AdminKey is null ? null : DateTimeOffset.UnixEpoch);
	public Task CreateOrUpdateAdminKeyAsync(Md5 key, CancellationToken cancellationToken = default) { AdminKey = key; return Task.CompletedTask; }
	public Task DeleteAdminKeyAsync(CancellationToken cancellationToken = default) { AdminKey = null; return Task.CompletedTask; }
}

sealed class Logins : ILoginRepository
{
	public int Count { get; private set; }
	public Task CreateAsync(Login login, CancellationToken cancellationToken = default) { Count++; return Task.CompletedTask; }
	public Task<Page<Login>> ListAsync(LoginQuery query, PageRequest page, CancellationToken cancellationToken = default) =>
		Task.FromResult(new Page<Login>([], Count));
}

sealed class Relationships : IRelationshipRepository
{
	public List<Relationship> Items { get; } = [];
	public Task CreateOrUpdateAsync(Relationship relationship, CancellationToken cancellationToken = default) { Items.Add(relationship); return Task.CompletedTask; }
	public Task DeleteAsync(Relationship relationship, CancellationToken cancellationToken = default) { Items.Remove(relationship); return Task.CompletedTask; }
	public Task<IReadOnlyList<Relationship>> ListAsync(User actor, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<Relationship>>(Items.Where(r => r.Actor.Equals(actor)).ToList());
}

sealed class ScoresFake : IScoreRepository
{
	private readonly HashSet<Md5> _checksums = [];
	public List<Score> Items { get; } = [];
	public Task<Score?> CreateAsync(ScoreData data, CancellationToken cancellationToken = default)
	{
		if (data.Checksum is { } c && !_checksums.Add(c)) return Task.FromResult<Score?>(null);
		var score = new Score { Id = Items.Count + 1, Value = data };
		Items.Add(score);
		return Task.FromResult<Score?>(score);
	}
	public ValueTask<Score?> GetAsync(int id, CancellationToken cancellationToken = default) => ValueTask.FromResult(Items.FirstOrDefault(s => s.Id == id));
	public Task<Page<Score>> ListAsync(ScoreQuery query, PageRequest page, CancellationToken cancellationToken = default) => Task.FromResult(new Page<Score>(Items, Items.Count));
}

sealed class Replays : IReplayStorage
{
	public int Saved { get; private set; }
	public Task SaveAsync(Score score, Stream content, CancellationToken cancellationToken = default) { Saved++; return Task.CompletedTask; }
	public Task<Stream?> OpenAsync(Score score, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
}

sealed class StatsFake : IUserStatsRepository
{
	private readonly Dictionary<(int, GameMode), UserStats> _items = new();
	public ValueTask<UserStats> GetAsync(User user, GameMode mode, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(_items.GetValueOrDefault((user.Id, mode)) ?? new UserStats { UserId = user.Id, Mode = mode });
	public Task CreateOrUpdateAsync(UserStats stats, CancellationToken cancellationToken = default) { _items[(stats.UserId, stats.Mode)] = stats; return Task.CompletedTask; }
}

sealed class Reader : IBeatmapsetReader
{
	public BeatmapsetArchive? Next { get; set; }
	public Task<BeatmapsetArchive?> ReadAsync(Stream archive, CancellationToken cancellationToken = default) => Task.FromResult(Next);
}

sealed class Analyser : IBeatmapAnalyser
{
	public BeatmapAnalysis Analyze(byte[] content, GameMode mode, GameMods mods) =>
		System.Text.Encoding.UTF8.GetString(content) == "fail" ? throw new InvalidDataException("bad map") : new(new Difficulty(mode, 120, TimeSpan.FromMinutes(1), 4, 9, 8, 5, 5.5), new OsuObjects());
}

sealed class Sets : IBeatmapsetRepository
{
	private readonly Dictionary<int, Beatmapset> _items = new();
	public ValueTask<Beatmapset?> GetAsync(int id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_items.GetValueOrDefault(id));
	public Task CreateOrUpdateAsync(Beatmapset set, CancellationToken cancellationToken = default) { _items[set.Id] = set; return Task.CompletedTask; }
	public Task<Page<Beatmapset>> ListAsync(BeatmapQuery query, PageRequest page, CancellationToken cancellationToken = default) =>
		Task.FromResult(new Page<Beatmapset>(_items.Values.OrderByDescending(s => s.Id).Skip(page.Offset).Take(page.Limit).ToList(), _items.Count));
	public Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default) { _items.Remove(set.Id); return Task.CompletedTask; }
}

sealed class Maps : IBeatmapRepository
{
	private readonly Dictionary<Md5, Beatmap> _items = new();
	public Task CreateOrUpdateAsync(Beatmap beatmap, CancellationToken cancellationToken = default) { _items[beatmap.Hash] = beatmap; return Task.CompletedTask; }
	public ValueTask<Beatmap?> GetAsync(int id, CancellationToken cancellationToken = default) => ValueTask.FromResult(_items.Values.FirstOrDefault(b => b.Id == id));
	public ValueTask<Beatmap?> GetByHashAsync(Md5 hash, CancellationToken cancellationToken = default) => ValueTask.FromResult(_items.GetValueOrDefault(hash));
	public Task<IReadOnlyList<Beatmap>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<Beatmap>>(_items.Values.Where(b => b.Beatmapset.Id == set.Id).ToList());
	public Task<Page<Beatmap>> ListAsync(BeatmapQuery query, PageRequest page, CancellationToken cancellationToken = default) => Task.FromResult(new Page<Beatmap>([], 0));
	public Task RetainAsync(Beatmapset set, IReadOnlyCollection<Beatmap> keep, CancellationToken cancellationToken = default)
	{
		foreach (var b in _items.Values.Where(b => b.Beatmapset.Id == set.Id && !keep.Any(k => k.Hash == b.Hash)).ToList()) _items.Remove(b.Hash);
		return Task.CompletedTask;
	}
}

sealed class Archives : IBeatmapsetStorage
{
	public HashSet<int> Stored { get; } = [];
	public Task SaveAsync(Beatmapset set, Stream content, CancellationToken cancellationToken = default) { Stored.Add(set.Id); return Task.CompletedTask; }
	public Task<Stream?> OpenAsync(Beatmapset set, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(Stored.Contains(set.Id) ? new MemoryStream() : null);
	public Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default) { Stored.Remove(set.Id); return Task.CompletedTask; }
}

sealed class Rounds : IRoundRepository
{
	public List<Round> Items { get; } = [];
	public Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public Task<IReadOnlyList<Round>> ListAsync(Match match, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<Round>>(Items.Where(r => r.Match.Equals(match)).ToList());
}

sealed class MatchEvents : IMatchEventRepository
{
	public List<MatchEvent> Items { get; } = [];
	public Task CreateAsync(MatchEvent matchEvent, CancellationToken cancellationToken = default) { Items.Add(matchEvent); return Task.CompletedTask; }
	public Task<IReadOnlyList<MatchEvent>> ListAsync(Match match, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<MatchEvent>>(Items.Where(e => e.Match.Equals(match)).ToList());
}
