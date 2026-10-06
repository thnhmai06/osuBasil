-- Basil baseline schema.
-- Conventions: PascalCase names; INTEGER for ints, booleans (0/1), enums and times (Unix
-- milliseconds UTC); TEXT for strings, MD5 (lowercase hex), URIs and IP addresses; STRICT tables.

create table Users
(
	Id          INTEGER PRIMARY KEY AUTOINCREMENT,
	Name        TEXT    NOT NULL,
	SafeName    TEXT    GENERATED ALWAYS AS (replace(lower(Name), ' ', '_')) STORED UNIQUE,
	Country     INTEGER NOT NULL,
	Permissions INTEGER NOT NULL,
	DeletedAt   INTEGER NULL
) strict;

create table Credentials
(
	UserId       INTEGER PRIMARY KEY REFERENCES Users (Id),
	PasswordHash TEXT NOT NULL
) strict;

create table Logins
(
	Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
	UserId              INTEGER NOT NULL REFERENCES Users (Id),
	Ip                  TEXT    NOT NULL,
	Timestamp           INTEGER NOT NULL,
	ClientDate          TEXT    NULL,
	ClientRevision      INTEGER NULL,
	ClientStream        INTEGER NULL,
	OsuPathHash         TEXT    NULL,
	NetworkAdapters     TEXT    NULL,
	NetworkAdaptersHash TEXT    NULL,
	UninstallHash       TEXT    NULL,
	DiskSignatureHash   TEXT    NULL
) strict;

create index Logins_UserId_Timestamp on Logins (UserId, Timestamp desc);

create table Restrictions
(
	Id          INTEGER PRIMARY KEY AUTOINCREMENT,
	UserId      INTEGER NOT NULL REFERENCES Users (Id),
	Permissions INTEGER NOT NULL,
	StartsAt    INTEGER NOT NULL,
	EndsAt      INTEGER NULL
) strict;

create index Restrictions_UserId on Restrictions (UserId);

create table Relationships
(
	ActorId   INTEGER NOT NULL REFERENCES Users (Id),
	TargetId  INTEGER NOT NULL REFERENCES Users (Id),
	Type      INTEGER NOT NULL,
	CreatedAt INTEGER NOT NULL,
	PRIMARY KEY (ActorId, TargetId)
) strict;

create table Channels
(
	Name             TEXT PRIMARY KEY,
	Topic            TEXT    NOT NULL,
	ReadPermissions  INTEGER NOT NULL,
	WritePermissions INTEGER NOT NULL,
	AutoJoin         INTEGER NOT NULL,
	Visible          INTEGER NOT NULL
) strict;

insert into Channels (Name, Topic, ReadPermissions, WritePermissions, AutoJoin, Visible)
values ('#osu', 'General discussion.', 0, 0, 1, 1),
       ('#lobby', 'Multiplayer lobby discussion.', 0, 0, 0, 1);

create table Settings
(
	Id                     INTEGER PRIMARY KEY CHECK (Id = 1),
	Motd                   TEXT    NULL,
	LockedCreation         INTEGER NOT NULL,
	MenuIconUrl            TEXT    NULL,
	MenuIconImage          TEXT    NULL,
	MirrorDownloadEndpoint TEXT    NULL,
	MirrorSearchEndpoint   TEXT    NULL
) strict;

-- 4 = CreationLocks.Beatmapsets.
insert into Settings (Id, Motd, LockedCreation, MenuIconUrl, MenuIconImage, MirrorDownloadEndpoint,
                      MirrorSearchEndpoint)
values (1, NULL, 4, NULL, NULL, NULL, NULL);

create table MenuBanners
(
	Image     TEXT PRIMARY KEY,
	Url       TEXT    NOT NULL,
	StartsAt  INTEGER NULL,
	EndsAt    INTEGER NULL,
	CreatedAt INTEGER NOT NULL
) strict;

create table Beatmapsets
(
	Id        INTEGER PRIMARY KEY,
	Artist    TEXT    NOT NULL,
	Title     TEXT    NOT NULL,
	Creator   TEXT    NOT NULL,
	CreatedAt INTEGER NOT NULL,
	UpdatedAt INTEGER NOT NULL,
	Locked    INTEGER NOT NULL,
	Visible   INTEGER NOT NULL
) strict;

-- Objects is the JSON of a Basil.Domain.Beatmaps.BeatmapObjects value; Length is in milliseconds.
create table Beatmaps
(
	Id           INTEGER PRIMARY KEY,
	BeatmapsetId INTEGER NOT NULL REFERENCES Beatmapsets (Id) ON DELETE CASCADE,
	Hash         TEXT    NOT NULL UNIQUE,
	Version      TEXT    NOT NULL,
	Mode         INTEGER NOT NULL,
	Star         REAL    NOT NULL,
	Length       INTEGER NOT NULL,
	Bpm          REAL    NOT NULL,
	Cs           REAL    NOT NULL,
	Ar           REAL    NOT NULL,
	Od           REAL    NOT NULL,
	Hp           REAL    NOT NULL,
	Objects      TEXT    NOT NULL,
	Locked       INTEGER NOT NULL,
	Visible      INTEGER NOT NULL
) strict;

create index Beatmaps_BeatmapsetId on Beatmaps (BeatmapsetId);
create index Beatmaps_Mode on Beatmaps (Mode);

create table Matches
(
	Id        INTEGER PRIMARY KEY AUTOINCREMENT,
	Name      TEXT    NOT NULL,
	CreatorId INTEGER NULL REFERENCES Users (Id),
	StartedAt INTEGER NOT NULL,
	EndedAt   INTEGER NULL,
	IsPrivate INTEGER NOT NULL
) strict;

create index Matches_EndedAt on Matches (EndedAt);

create table Rounds
(
	MatchId      INTEGER NOT NULL REFERENCES Matches (Id),
	Number       INTEGER NOT NULL,
	BeatmapHash  TEXT    NOT NULL,
	Mode         INTEGER NOT NULL,
	Mods         INTEGER NOT NULL,
	Freemods     INTEGER NOT NULL,
	TeamType     INTEGER NOT NULL,
	WinCondition INTEGER NOT NULL,
	Seed         INTEGER NOT NULL,
	StartedAt    INTEGER NOT NULL,
	EndedAt      INTEGER NULL,
	Aborted      INTEGER NOT NULL,
	PRIMARY KEY (MatchId, Number)
) strict;

create table MatchEvents
(
	Id        INTEGER PRIMARY KEY AUTOINCREMENT,
	MatchId   INTEGER NOT NULL REFERENCES Matches (Id),
	Type      INTEGER NOT NULL,
	Timestamp INTEGER NOT NULL,
	ActorId   INTEGER NULL REFERENCES Users (Id),
	TargetId  INTEGER NULL REFERENCES Users (Id),
	Detail    TEXT    NULL
) strict;

create index MatchEvents_MatchId on MatchEvents (MatchId);

-- The round is not a foreign key: rounds are written asynchronously, so a score can arrive first.
create table Scores
(
	Id          INTEGER PRIMARY KEY AUTOINCREMENT,
	UserId      INTEGER NULL REFERENCES Users (Id),
	BeatmapHash TEXT    NULL,
	Mode        INTEGER NOT NULL,
	Mods        INTEGER NOT NULL,
	Num300      INTEGER NOT NULL,
	Num100      INTEGER NOT NULL,
	Num50       INTEGER NOT NULL,
	NumGeki     INTEGER NOT NULL,
	NumKatu     INTEGER NOT NULL,
	NumMiss     INTEGER NOT NULL,
	TotalScore  INTEGER NOT NULL,
	MaxCombo    INTEGER NOT NULL,
	Grade       INTEGER NOT NULL,
	IsPassed    INTEGER NOT NULL,
	IsFullCombo INTEGER NOT NULL,
	Timestamp   INTEGER NOT NULL,
	MatchId     INTEGER NULL,
	RoundNumber INTEGER NULL,
	Team        INTEGER NULL,
	Checksum    TEXT    NULL UNIQUE
) strict;

create index Scores_UserId on Scores (UserId);
create index Scores_BeatmapHash on Scores (BeatmapHash);
create index Scores_MatchId_RoundNumber on Scores (MatchId, RoundNumber);

create table UserStats
(
	UserId      INTEGER NOT NULL REFERENCES Users (Id),
	Mode        INTEGER NOT NULL,
	TotalScore  INTEGER NOT NULL,
	RankedScore INTEGER NOT NULL,
	PlayCount   INTEGER NOT NULL,
	PRIMARY KEY (UserId, Mode)
) strict;
