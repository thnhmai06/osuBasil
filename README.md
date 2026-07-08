<div align="center">

<img src="assets/icon.png" width="160" alt="Basil">

# Basil

<sub><i>Nếu [Akatsuki](https://github.com/osuAkatsuki) có nghĩa là "bình minh", thì Basil là bông hướng dương luôn hướng về Mặt Trời.</i></sub>

**Một server [osu!](https://osu.ppy.sh/) (stable) nhẹ, nhằm phục vụ thi đấu multiplayer/tournament qua LAN - hoàn toàn offline, không phụ thuộc vào internet.**

[![CI](https://img.shields.io/github/actions/workflow/status/thnhmai06/osuBasil/ci.yml?branch=main&label=CI&style=flat-square)](https://github.com/thnhmai06/osuBasil/actions)
[![License](https://img.shields.io/github/license/thnhmai06/osuBasil?style=flat-square)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white&style=flat-square)](https://dotnet.microsoft.com/)
[![Last commit](https://img.shields.io/github/last-commit/thnhmai06/osuBasil?style=flat-square)](https://github.com/thnhmai06/osuBasil/commits/main)

</div>

> [!IMPORTANT]
> **Miễn trừ trách nhiệm.** 
> Dự án này không liên kết, không được xác nhận, và không có bất kỳ liên hệ nào với [osu!](https://osu.ppy.sh/) ([ppy Pty Ltd](https://ppy.sh/)) hay [bancho.py](https://github.com/osuAkatsuki/bancho.py) ([Akatsuki](https://github.com/osuAkatsuki)). "Basil" chỉ là một cái tên tham chiếu. Hình ảnh mascot là nhân vật Basil đến từ [OMORI](https://www.omori-game.com/) - dự án này không liên kết, không được xác nhận, và không có bất kỳ liên hệ nào với OMORI hay nhà phát triển của nó, [OMOCAT](https://omocat.com/). Mọi quyền đối với nhân vật thuộc về chủ sở hữu tương ứng.

## Các tính năng chính

- Cung cấp [**môi trường multiplayer**](https://osu.ppy.sh/wiki/en/Client/Interface/Multiplayer) giống với [osu!Bancho](https://osu.ppy.sh/wiki/en/Bancho_%28server%29), **loại bỏ** hệ thống xử lý Singleplayer và các tính năng không liên quan.
- Hỗ trợ [**osu!direct**](https://osu.ppy.sh/community/forums/topics/1433039), [**osu!tourney**](https://osu.ppy.sh/wiki/en/osu%21_tournament_client/osu%21tourney), [**BanchoBot**](https://osu.ppy.sh/wiki/en/BanchoBot), [**IRC**](https://osu.ppy.sh/wiki/en/Community/Internet_Relay_Chat) và các **tính năng Xã hội cơ bản**.
- Quản lý **Users, Beatmaps, Scores, Matches, Replays, Seasonal Backgrounds, FAQs** ngay trên CSDL/ổ đĩa.
- **Không phụ thuộc** vào [osu!api](https://osu.ppy.sh/wiki/en/osu%21api), dịch vụ mirror hay các dịch vụ bên thứ 3. Các thông số (như Star Rating) được tính toán cục bộ và lưu trữ trong CSDL. **Tất cả đều offline và local 100%**.
- **Cung cấp các API** để theo dõi trực tiếp dữ liệu trận đấu, input của người chơi và các sự kiện diễn ra trong trận đấu.

## Tech stack

| Layer | Lựa chọn |
| --- | --- |
| Runtime | [.NET](https://dot.net/) 10 với [ASP.NET](https://asp.net/) |
| Database | [MySQL](https://www.mysql.com/) 8, truy cập qua [Dapper](https://github.com/DapperLib/Dapper), schema quản lý bởi [DbUp](https://dbup.readthedocs.io/) |
| Cache | [Redis](https://redis.io/) 7, qua [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) |
| Star rating | Tham chiếu đến các thuật toán tính toán trực tiếp của [osu!lazer](https://github.com/ppy/osu) |
| Test | [xUnit](https://xunit.net/), [NetArchTest](https://github.com/BenMorris/NetArchTest) để enforce ranh giới layer, [Testcontainers](https://testcontainers.com/) cho integration test MySQL thật |

## Credits

**Basil** được xây dựng trên nền tảng của [**bancho.py**](https://github.com/osuAkatsuki/bancho.py) bởi [Akatsuki](https://github.com/osuAkatsuki). 

Cảm ơn rất nhiều đội ngũ [Akatsuki](https://github.com/osuAkatsuki) về dự án tâm huyết này của họ!

## Star History

[![Star History Chart](https://api.star-history.com/chart?repos=thnhmai06/osuBasil&type=date&legend=top-left&sealed_token=wPQ_eLQYxDpC8IxGbg3aO7Pj4XQ1Pxr5Y16JLxzXZkGFuytVDcgJBdCUlsx9wbZzySsHPkAAj3L9OO5nOCpSebEGkL8fFpPoUwZSSgEHqj1RSWZgLn_G2Vuqc0itECn1WFYXPG74tJN9U1OzQoMcvyLnW8NBycp-yaxWQmDu-rlmTRVhvMpW3LGys9r1)](https://www.star-history.com/?repos=thnhmai06%2FosuBasil&type=date&legend=top-left)