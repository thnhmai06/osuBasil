-- Basil schema (SQLite). PascalCase tables/columns, ids autoincrement from 1.
-- Scope: multiplayer/tournament only. Cut vs. upstream bancho.py: Clans, Achievements,
-- UserAchievements, Comments, Favourites, MapRequests, PerformanceReports, Startups, Ratings
-- (no consumer anywhere in the codebase — rating submission was never built, so the table could
-- never hold real data). Kept: Relationships (social), ClientHashes/IngameLogins/Logs
-- (anticheat log-only). New: Matches/Rounds (1 multiplayer room = 1 Match, 1 beatmap played
-- within it = 1 Round), Scores repurposed to link to a Round instead of standing alone.

create table Users
(
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Name          varchar(32)                                   not null,
    SafeName      varchar(32)                                   not null,
    Priv          int      default 1                            not null,
    PwBcrypt      char(60)                                      not null,
    Country       char(2)  default 'xx'                         not null,
    SilenceEnd    datetime default '1970-01-01 00:00:00'        not null,
    constraint Users_Name_uindex unique (Name),
    constraint Users_SafeName_uindex unique (SafeName)
);
create index Users_Priv_index on Users (Priv);

-- Fixed gameplay stats: seeded once with default values, never UPDATEd on score submission
-- (server does not track singleplayer ranking/progression).
create table UserStats
(
    Id     int not null,
    Mode   int not null,
    Tscore bigint      default 0     not null,
    Rscore bigint      default 0     not null,
    Plays  int         default 0     not null,
    Acc    float(6, 3) default 0.000 not null,
    primary key (Id, Mode),
    constraint UserStats_Users_Id_fk foreign key (Id) references Users (Id)
);

-- One row per beatmap set. No osu!api staleness tracking (server runs fully offline; sets are
-- only ever added via local ingestion, see BeatmapIngestionService/BeatmapWatcherService).
-- Artist/Title/Creator/Status/LastUpdate are shared by every difficulty in the set, so they
-- live here instead of being duplicated onto each Beatmaps row. CreatedAt is first-ingestion
-- time, distinct from LastUpdate.
create table Mapsets
(
    Id         int          not null primary key,
    Artist     varchar(128) not null,
    Title      varchar(128) not null,
    Creator    varchar(19)  not null,
    LastUpdate datetime     not null,
    CreatedAt  datetime     not null
);

create table Beatmaps
(
    Id          int                   not null primary key,
    MapsetId    int                   not null,
    Md5         char(32)              not null,
    Version     varchar(128)          not null,
    Filename    varchar(256)          not null,
    TotalLength int                   not null,
    MaxCombo    int                   not null,
    Frozen      boolean default false not null,
    Plays       int     default 0     not null,
    Passes      int     default 0     not null,
    Mode        int                   not null,
    Bpm         float(12, 2) default 0.00 not null,
    Cs          float(4, 2)  default 0.00 not null,
    Ar          float(4, 2)  default 0.00 not null,
    Od          float(4, 2)  default 0.00 not null,
    Hp          float(4, 2)  default 0.00 not null,
    Sr          float(6, 3)  default 0.000 not null,
    constraint Beatmaps_Md5_uindex unique (Md5),
    constraint Beatmaps_Mapsets_Id_fk foreign key (MapsetId) references Mapsets (Id) on delete cascade
);
create index Beatmaps_MapsetId_index on Beatmaps (MapsetId);
create index Beatmaps_Filename_index on Beatmaps (Filename);
create index Beatmaps_Mode_index on Beatmaps (Mode);

create table Channels
(
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name      varchar(32)           not null,
    Topic     varchar(256)          not null,
    ReadPriv  int     default 1     not null,
    WritePriv int     default 2     not null,
    AutoJoin  boolean default false not null,
    constraint Channels_Name_uindex unique (Name)
);
create index Channels_AutoJoin_index on Channels (AutoJoin);

create table Relationships
(
    User1 int not null,
    User2 int not null,
    Type  varchar(10) not null check (Type in ('friend', 'block')),
    primary key (User1, User2)
);

create table ClientHashes
(
    UserId      int           not null,
    OsuPathMd5  char(32)      not null,
    Adapters    char(32)      not null,
    UninstallId char(32)      not null,
    DiskSerial  char(32)      not null,
    LastSeenAt  datetime      not null,
    Occurrences int default 0 not null,
    primary key (UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial)
);

create table IngameLogins
(
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId     int         not null,
    Ip         varchar(45) not null,
    OsuVer     date        not null,
    OsuStream  varchar(11) not null,
    LoggedInAt datetime    not null
);

-- CreatedAt is always supplied by the app on insert (see SqliteLogRepository) — never UPDATEd
-- afterwards, so no ON UPDATE trigger is needed (MySQL's `on update CURRENT_TIMESTAMP` here was
-- dead weight; nothing ever updates a Logs row).
create table Logs
(
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    FromId    int           not null,
    ToId      int           not null,
    Action    varchar(32)   not null,
    Msg       varchar(2048) null,
    CreatedAt datetime      not null
);

-- One multiplayer room = one Match. EndedAt is null while the room is still open in-memory.
-- Mode/WinCondition/TeamType moved to Rounds (can change between rounds).
-- HostId removed — tracked via MatchEvents instead.
create table Matches
(
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Name      varchar(50) not null,
    CreatedAt datetime    not null,
    EndedAt   datetime null
);

-- One beatmap played within a Match = one Round. WinningTeam is intentionally NOT stored here —
-- it's computed on read (TRT generation) from whatever Scores rows exist for the round, since
-- score submission and MatchComplete arrive on separate connections with no ordering guarantee.
-- Mode/WinCondition/TeamType/beatmap fields denormalized per round for self-contained TRT.
create table Rounds
(
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    MatchId        int      not null,
    RoundIndex     int      not null,
    BeatmapId      int      not null,
    MapMd5         char(32) not null,
    Mode           int      not null,
    WinCondition   int      not null,
    TeamType       int      not null,
    BeatmapArtist  varchar(128) not null default '',
    BeatmapTitle   varchar(128) not null default '',
    BeatmapVersion varchar(128) not null default '',
    BeatmapCreator varchar(64)  not null default '',
    Aborted        boolean not null default 0,
    Mods           int      not null,
    StartedAt      datetime not null,
    EndedAt        datetime null,
    constraint Rounds_Matches_Id_fk foreign key (MatchId) references Matches (Id)
);
create index Rounds_MatchId_index on Rounds (MatchId);

-- MapMd5/Mode kept denormalised (not just via Round) so the solo-leaderboard-shaped read paths
-- (osu-osz2-getscores.php) can keep querying by map without joining through Rounds. RoundId/Team
-- are null for a score submitted outside any match (not linked to a Round).
create table Scores
(
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    RoundId         int null,
    Team            int null,
    MapMd5          char(32) not null,
    Score           bigint   not null,
    Accuracy        float(6, 3) not null,
    MaxCombo        int      not null,
    Mods            int      not null,
    N300            int      not null,
    N100            int      not null,
    N50             int      not null,
    NMiss           int      not null,
    NGeki           int      not null,
    NKatu           int      not null,
    Grade           varchar(2) default 'N' not null,
    Mode            int      not null,
    PlayTime        datetime not null,
    TimeElapsed     int      not null,
    ClientFlags     int      not null,
    UserId          int      not null,
    Perfect         boolean  not null,
    OnlineChecksum  char(32) not null,
    SubmittedAt     datetime not null,
    constraint Scores_Rounds_Id_fk foreign key (RoundId) references Rounds (Id)
);
create index Scores_MapMd5_index on Scores (MapMd5);
create index Scores_Score_index on Scores (Score);
create index Scores_Mode_index on Scores (Mode);
create index Scores_UserId_index on Scores (UserId);
create index Scores_OnlineChecksum_index on Scores (OnlineChecksum);
create index Scores_RoundId_index on Scores (RoundId);

-- Chronological log of match lifecycle events. ActorUserId is null for system actions
-- (e.g. server shutdown recovery, external API). Usernames denormalised for self-contained TRT.
create table MatchEvents
(
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    MatchId        int       not null,
    EventType      int       not null,
    ActorUserId    int       null,
    ActorUserName  varchar(32) null,
    TargetUserId   int       null,
    TargetUserName varchar(32) null,
    Timestamp      datetime  not null,
    Detail         varchar(512) null,
    constraint MatchEvents_Matches_Id_fk foreign key (MatchId) references Matches (Id)
);
create index MatchEvents_MatchId_index on MatchEvents (MatchId);

insert into Users (Id, Name, SafeName, Priv, Country, PwBcrypt)
values (0, 'BasilBot', 'basilbot', 1, 'vn',
        '_______________________my_cool_bcrypt_______________________');

insert into UserStats (Id, Mode)
values (0, 0);
insert into UserStats (Id, Mode)
values (0, 1);
insert into UserStats (Id, Mode)
values (0, 2);
insert into UserStats (Id, Mode)
values (0, 3);
insert into UserStats (Id, Mode)
values (0, 4);
insert into UserStats (Id, Mode)
values (0, 5);
insert into UserStats (Id, Mode)
values (0, 6);
insert into UserStats (Id, Mode)
values (0, 8);

insert into Channels (Name, Topic, ReadPriv, WritePriv, AutoJoin)
values ('#osu', 'General discussion.', 1, 2, true),
       ('#lobby', 'Multiplayer lobby discussion room.', 1, 2, false);
