# Run & Deployment

## Dành cho Triển khai

```bash
dotnet publish src/Basil.Web -c Release -r win-x64 --self-contained false -o publish/win-x64
# hoặc:
dotnet publish src/Basil.Web -c Release -r linux-x64 --self-contained false -o publish/linux-x64
```

Sau đó chạy executable vừa publish (`Basil.Web.exe` trên Windows, `Basil.Web` trên Linux) từ thư mục `publish/<rid>/`.
Máy chạy cần cài sẵn **ASP.NET Core Runtime 10** (không self-contained, không bundle runtime vào exe).

### Nó làm gì?

1. Executable tự tạo file SQLite (`basil.db`, mặc định) ngay cạnh chính nó nếu chưa tồn tại.
2. `SqlMigrationRunner` áp dụng schema `001_base.sql` được nhúng sẵn tự động khi khởi động qua [DbUp](https://dbup.readthedocs.io/) để thực hiện tự động migration — không cần bước migration thủ công.
3. Ngay sau đó, app cũng nạp mọi file beatmap được thả vào folder `Mapsets/` và bootstrap session BanchoBot.

App lắng nghe tại `http://localhost:<port do ASP.NET Core chọn>` theo mặc định — override qua `--urls` hoặc biến môi trường `ASPNETCORE_URLS` (ví dụ `--urls "http://*:8080"`).

### Các biến Môi trường tùy chỉnh

ASP.NET Core đọc `Section__Key` trực tiếp từ biến môi trường (không qua lớp dịch nào cả — không còn docker-compose để làm việc đó). Xem [`.env.example`](../.env.example) để có danh sách đầy đủ; các biến chính:

| Biến                       | Mục đích                                                                                                          |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `ServerBehavior__Domain`   | domain mà server tự nhận diện (dùng trong link icon menu, v.v.)                                                    |
| `ServerBehavior__CommandPrefix` | tiền tố lệnh chat (`!help`, `!roll`, `!mp ...`)                                                               |
| `ServerBehavior__MenuIconUrl` / `ServerBehavior__MenuOnclickUrl` | icon menu chính trong game và URL click-through của nó                                       |
| `Bot__Name`                | tên hiển thị của BanchoBot                                                                                        |
| `Api__AdminKey`            | khoá các route management CRUD của `api.<domain>` qua `X-Admin-Key`; để trống để giữ chúng khoá (401 cho mọi thứ) |
| `Database__Path`           | đường dẫn file SQLite - mặc định `basil.db` cạnh executable, override được nếu muốn đặt chỗ khác                  |

**Không** có biến môi trường nào cho đường dẫn lưu trữ file (replay/avatar/beatmap/seasonal/faq) — 5 folder đó cố định, xem mục Dữ liệu bên dưới.

### Dữ liệu

Tất cả nằm cố định ngay cạnh thư mục chứa executable, không cấu hình được:

| Đường dẫn (cạnh exe) | Mục đích                                                                                                |
| --------------------- | -------------------------------------------------------------------------------------------------------- |
| `basil.db`             | File database SQLite (tự tạo lúc khởi động lần đầu, kèm `basil.db-wal`/`basil.db-shm` khi đang chạy)    |
| `Replays/`             | Lưu replay files                                                                                       |
| `Avatars/`             | Lưu avatar files                                                                                       |
| `Mapsets/`             | Lưu beatmap files (`.osu`/`.osz`) - thả file vào đây để chúng được nạp tự động ở lần khởi động kế tiếp |
| `Seasonals/`           | Lưu seasonal data                                                                                       |
| `Faqs/`                | Lưu nội dung `!faq`                                                                                     |

Hoặc để nạp beatmap mà không cần restart, sử dụng `POST /beatmaps` trên host `api.` (khoá bằng admin-key).

Muốn di dời sang máy khác: dừng server, copy nguyên thư mục chứa executable (bao gồm `basil.db*` và 5 folder trên) sang máy đích, chạy lại.

## Dành cho Phát triển

### Chạy Server

```bash
dotnet run --project src/Basil.Web
```

Không cần khởi động thêm service nào khác — database là 1 file SQLite tự tạo cạnh binary output (`src/Basil.Web/bin/Debug/net10.0/basil.db`), migration tự chạy lúc khởi động.

> [!IMPORTANT]
> `Basil.Infrastructure.Tests` dựng lên một file SQLite tạm cho mỗi test class, xoá ngay sau khi test xong - không cần Docker daemon hay service ngoài nào. Xem [`CLAUDE.md`](../CLAUDE.md) về khuyến nghị chạy project test này ở foreground.

### Kết nối osu! client đến server

> [!IMPORTANT]
> Client osu! stable chỉ kết nối qua **HTTPS (port 443)** tới `c./ce./c4./c5./c6./osu./b./a./api.<domain-của-bạn>` - HTTP thường sẽ bị từ chối âm thầm trước khi tới được Kestrel.

Để giải quyết vấn đề này, bạn cần:

1.  Khởi động server như trên.
2.  Tạo một certificate self-signed có danh sách SAN bao phủ **domain và cả 9 subdomain**:

    | Subdomain         | Mục đích                             |
    | ----------------- | ------------------------------------ |
    | `basil.local`     | Domain chính                         |
    | `c.basil.local`   | Bancho binary protocol (Bank server) |
    | `ce.basil.local`  | Bancho binary protocol (Chooses)     |
    | `c4.basil.local`  | Bancho binary protocol (v4)          |
    | `c5.basil.local`  | Bancho binary protocol (v5)          |
    | `c6.basil.local`  | Bancho binary protocol (v6)          |
    | `osu.basil.local` | HTTP endpoints (Legacy web API)      |
    | `b.basil.local`   | Beatmap thumbnails                   |
    | `a.basil.local`   | Avatar files                         |
    | `api.basil.local` | Tournament API & management          |

    Sau đó, hãy tạo certificate:
    - **PowerShell (Windows):**

      ```powershell
      $dnsNames = @("basil.local", "c.basil.local", "ce.basil.local", "c4.basil.local", "c5.basil.local", "c6.    basil.local", "osu.basil.local", "b.basil.local", "a.basil.local", "api.basil.local")
      $cert = New-SelfSignedCertificate -DnsName $dnsNames -CertStoreLocation "cert:\LocalMachine\My"     -KeyExportPolicy Exportable
      Export-PfxCertificate -Cert "cert:\LocalMachine\My\$($cert.Thumbprint)" -FilePath "basil-cert.pfx"  -Password (ConvertTo-SecureString -String "your-password" -Force -AsPlainText)
      Import-PfxCertificate -FilePath "basil-cert.pfx" -CertStoreLocation "cert:\LocalMachine\Root" -Password     (ConvertTo-SecureString -String "your-password" -Force -AsPlainText)
      ```

    - **Bash (macOS/Linux):**
      ```bash
      openssl req -new -x509 -days 365 -nodes -out basil-cert.pem -keyout basil-key.pem -subj "/CN=basil. local"    \
        -addext "subjectAltName=DNS:basil.local,DNS:c.basil.local,DNS:ce.basil.local,DNS:c4.basil.local,  DNS:c5.    basil.local,DNS:c6.basil.local,DNS:osu.basil.local,DNS:b.basil.local,DNS:a.basil.local,    DNS:api.basil.   local"
      openssl pkcs12 -export -out basil-cert.pfx -inkey basil-key.pem -in basil-cert.pem -password        pass:your-password
      # Trên macOS, thêm certificate vào Keychain: security add-trusted-cert -d -r trustRoot -k ~/Library/    Keychains/login.keychain basil-cert.pem
      # Trên Linux, tùy thuộc vào distro (xem các bước cài certificate của hệ thống bạn)
      ```

    > [!NOTE]
    > Client osu! stable kiểm tra chính xác subdomain nó kết nối tới, vậy nên một cert dev đơn giản `CN=localhost` sẽ fail.

3.  Trỏ Kestrel tới file `.pfx` đó qua biến môi trường:

    **PowerShell:**

    ```powershell
    $env:Kestrel__Certificates__Default__Path = "<đường dẫn tuyệt đối tới basil-cert.pfx>"
    $env:Kestrel__Certificates__Default__Password = "your-password"
    ```

    **Bash:**

    ```bash
    export Kestrel__Certificates__Default__Path="<đường dẫn tuyệt đối tới basil-cert.pfx>"
    export Kestrel__Certificates__Default__Password="your-password"
    ```

    Biến môi trường override `appsettings.*.json` theo thứ tự ưu tiên config mặc định của ASP.NET Core.

4.  Chạy

    ```bash
    `dotnet run --project src/Basil.Web --urls "http://*:80;https://*:443"`
    ```

    từ một terminal **elevated** (bind port 80/443 cần quyền Admin trên Windows).

5.  Thêm domain và cả 9 subdomain vào hosts file:
    - **Windows (PowerShell):**

    ```powershell
    $hostsPath = "C:\Windows\System32\drivers\etc\hosts"
    $entries = @(
        "127.0.0.1 basil.local",
        "127.0.0.1 c.basil.local",
        "127.0.0.1 ce.basil.local",
        "127.0.0.1 c4.basil.local",
        "127.0.0.1 c5.basil.local",
        "127.0.0.1 c6.basil.local",
        "127.0.0.1 osu.basil.local",
        "127.0.0.1 b.basil.local",
        "127.0.0.1 a.basil.local",
        "127.0.0.1 api.basil.local"
    )
    Add-Content -Path $hostsPath -Value "`n$($entries -join "`n")" -Encoding UTF8
    ```

    - **Linux/macOS (Bash):**

    ```bash
    sudo tee -a /etc/hosts << EOF
    127.0.0.1 basil.local
    127.0.0.1 c.basil.local
    127.0.0.1 ce.basil.local
    127.0.0.1 c4.basil.local
    127.0.0.1 c5.basil.local
    127.0.0.1 c6.basil.local
    127.0.0.1 osu.basil.local
    127.0.0.1 b.basil.local
    127.0.0.1 a.basil.local
    127.0.0.1 api.basil.local
    EOF
    ```

6.  Chạy client với `osu!.exe --debug -devserver basil.local` (Windows) hoặc tương đương trên nền tảng khác.

> [!IMPORTANT]
> Chưa có endpoint đăng ký trong game (`POST /users` trên host `osu.` chỉ là stub), nên tạo tài khoản test trực tiếp trong file `basil.db` (dùng công cụ SQLite bất kỳ, ví dụ `sqlite3 basil.db` hoặc DB Browser for SQLite) - một hàng trong `Users` (với `Priv=3`, và `PwBcrypt` set thành bcrypt hash của **digest MD5 mã hoá hex** của mật khẩu - không phải digest thô, khớp với chính scheme mật khẩu của bancho.py) cộng một hàng cho mỗi game mode (`0,1,2,3,4,5,6,8` - mode `7` không tồn tại) trong `UserStats` - hoặc qua `POST /users` trên host `api.` (khoá bằng admin-key).

> [!IMPORTANT]
> Khi không có reverse proxy nginx phía trước cục bộ, server tự tổng hợp `X-Forwarded-For`/`X-Real-IP` từ địa chỉ remote của kết nối thô khi không có header nào trong hai header đó - nếu không, `Basil.Domain.ClientIpResolver.Resolve` sẽ throw, vì nó giả định (giống bancho.py) rằng một proxy luôn set các header này trong production.

### Chạy test

```bash
dotnet test tests/Basil.Domain.Tests
dotnet test tests/Basil.Protocol.Tests
dotnet test tests/Basil.Application.Tests
dotnet test tests/Basil.ArchitectureTests
dotnet test tests/Basil.IntegrationTests
dotnet test tests/Basil.Infrastructure.Tests
```
