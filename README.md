<div align="center">

<img src="assets/icon.png" width="160" alt="Basil">

# Basil

<sub><i>If [Akatsuki](https://github.com/osuAkatsuki) means "dawn", then Basil is the sunflower always facing the Sun.</i></sub>

**A lightweight [osu!](https://osu.ppy.sh/) (stable) server for multiplayer/tournament play over LAN — fully offline, no internet dependency.**

[![CI](https://img.shields.io/github/actions/workflow/status/thnhmai06/osuBasil/ci.yml?branch=main&label=CI&style=flat-square)](https://github.com/thnhmai06/osuBasil/actions)
[![License](https://img.shields.io/github/license/thnhmai06/osuBasil?style=flat-square)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white&style=flat-square)](https://dotnet.microsoft.com/)
[![Last commit](https://img.shields.io/github/last-commit/thnhmai06/osuBasil?style=flat-square)](https://github.com/thnhmai06/osuBasil/commits/main)

</div>

> [!IMPORTANT]
> **Disclaimer.**
> This project is not affiliated with, endorsed by, or connected to [osu!](https://osu.ppy.sh/) ([ppy Pty Ltd](https://ppy.sh/)) or [bancho.py](https://github.com/osuAkatsuki/bancho.py) ([Akatsuki](https://github.com/osuAkatsuki)). "Basil" is a reference name only. The mascot art depicts the character Basil from [OMORI](https://www.omori-game.com/) — this project is not affiliated with, endorsed by, or connected to OMORI or its developer, [OMOCAT](https://omocat.com/). All character rights belong to their respective owners.

## Key features

- Provides a [**multiplayer environment**](https://osu.ppy.sh/wiki/en/Client/Interface/Multiplayer) matching [osu!Bancho](https://osu.ppy.sh/wiki/en/Bancho_%28server%29), with singleplayer processing and unrelated features **removed**.
- Supports [**osu!direct**](https://osu.ppy.sh/community/forums/topics/1433039), [**osu!tourney**](https://osu.ppy.sh/wiki/en/osu%21_tournament_client/osu%21tourney), [**BanchoBot**](https://osu.ppy.sh/wiki/en/BanchoBot), [**IRC**](https://osu.ppy.sh/wiki/en/Community/Internet_Relay_Chat), and **basic social features**.
- Manages **Users, Beatmaps, Scores, Matches, Replays, Seasonal Backgrounds, FAQs** directly via database/filesystem.
- **No dependencies** on [osu!api](https://osu.ppy.sh/wiki/en/osu%21api), mirror services, or third-party services. Parameters (such as Star Rating) are computed locally and stored in the database. **100% offline and local**.
- **Provides APIs** for live match data, player input, and match event tracking.

## Tech stack

| Layer | Choice |
| --- | --- |
| Runtime | [.NET](https://dot.net/) 10 with [ASP.NET](https://asp.net/), runs as a standalone executable — no Docker required |
| Database | [SQLite](https://www.sqlite.org/) (1 file next to the executable), accessed via [Dapper](https://github.com/DapperLib/Dapper), schema managed by [DbUp](https://dbup.readthedocs.io/) |
| Star rating | References [osu!lazer](https://github.com/ppy/osu)'s own calculation algorithms directly |
| Test | [xUnit](https://xunit.net/), [NetArchTest](https://github.com/BenMorris/NetArchTest) to enforce layer boundaries |

## Credits

**Basil** is built on top of [**bancho.py**](https://github.com/osuAkatsuki/bancho.py) by [Akatsuki](https://github.com/osuAkatsuki).

Many thanks to the [Akatsuki](https://github.com/osuAkatsuki) team for their dedicated work on that project!

## Star History

[![Star History Chart](https://api.star-history.com/chart?repos=thnhmai06/osuBasil&type=date&legend=top-left&sealed_token=wPQ_eLQYxDpC8IxGbg3aO7Pj4XQ1Pxr5Y16JLxzXZkGFuytVDcgJBdCUlsx9wbZzySsHPkAAj3L9OO5nOCpSebEGkL8fFpPoUwZSSgEHqj1RSWZgLn_G2Vuqc0itECn1WFYXPG74tJN9U1OzQoMcvyLnW8NBycp-yaxWQmDu-rlmTRVhvMpW3LGys9r1)](https://www.star-history.com/?repos=thnhmai06%2FosuBasil&type=date&legend=top-left)
