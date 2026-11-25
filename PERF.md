# Performance

How Basil is measured, and the ledger of every tuning attempt. Nothing is tuned by guess: each change is measured the same
way before and after, kept only when it beats the run-to-run variance, and reverted otherwise. Reverted attempts stay in
the ledger so nobody tries them again without a new reason.

## The harness

`tools/LoadHarness` drives a running server through nginx the way real clients do:

* **osu!** clients sign in on `c.<domain>`, then poll once a second with a ping, a chat message about every 20 polls and a
  status change about every 45;
* **IRC** clients register over TLS on port 6697, join `#osu`, send a message every 5 s and measure the round trip of a PING;
* **API** callers sign in once on `api.<domain>`, then read a user, rooms and scores lists once a second.

It reports requests, failures by reason, p50, p95 and p99 latency and throughput per kind, and samples the server
container's CPU and memory when `--container` is given. The ramp is not measured; a failed sign-in or connect is counted
even during the ramp, under `osu-login`, `api-login` or `irc`.

### Accounts

Every client uses its own account (`name,password` per line). Create them against the running stack, once:

```bash
echo "name,password" > accounts.csv
for i in $(seq -w 1 270); do
  docker compose exec -T basil ./Basil users create --username "load$i" --password "load-pass-$i"
  echo "load$i,load-pass-$i" >> accounts.csv
done
```

`accounts.csv` holds passwords: keep it out of the repository.

### nginx limits

nginx limits requests and connections per address (`docker/nginx/nginx.conf`), and the harness comes from one address, so
the default limits would answer it with 429. For a run, mount a copy with the limits raised in place of the shipped file,
and never commit it:

```bash
sed -E 's/rate=[0-9]+r\/(s|m)/rate=100000r\/s/; s/limit_conn (http_conn|irc_conn) [0-9]+/limit_conn \1 60000/' \
  docker/nginx/nginx.conf > docker-data/nginx-load.conf
```

Then point the `nginx.conf` volume of the `nginx` service at `./docker-data/nginx-load.conf` (a
`docker-compose.override.yml` does it), and run `docker compose up -d`. Restore the shipped file afterwards.

### Running it

```bash
dotnet run --project tools/LoadHarness -c Release -- \
  --accounts accounts.csv --domain basil.local --insecure \
  --osu 200 --irc 50 --api 20 --ramp 60s --duration 120s \
  --container osubasil-basil-1 --out load-result.json
```

`--insecure` accepts the self-signed `basil.local` certificate; leave it off against a real certificate. `--connect <ip>`
reaches a server whose domain does not resolve, and `--irc-host` points IRC elsewhere. The exit code is 1 when any request
failed. Use `docker ps` for the container name.

## The fixed scenario

Every comparison uses this run on the same machine, with the same data, started from a freshly restarted stack:

| Part | Value |
|---|---|
| osu! clients | 200 (poll 1 s) |
| IRC clients | 50 (message every 5 s) |
| API callers | 20 (call every 1 s) |
| Ramp | 60 s, not measured (a 20 s ramp saturates 4 cores with sign-ins and sheds some of them) |
| Measured | 120 s |
| Server | the Compose stack, the image under test, nothing else running on the machine |

Run it three times for each state (baseline and each change). The spread of the three runs is the run-to-run variance; a
change counts only when its median beats the baseline median by more than that spread, on p95 latency, failures or CPU at
the same request rate.

## Ledger

One row per attempt, kept or reverted. Baseline and result are the medians of three runs, as
`osu p95 ms / irc p95 ms / api p95 ms / server CPU % avg / memory MiB max / failed`.

| # | Idea | Baseline | Result | Verdict | Why |
|---|---|---|---|---|---|
| 0 | Baseline (image `e91917168`, 4 cores, 2026-11-25) | 1.98 / 1.63 / 2.87 / 12.2 / 195 / 0 | - | - | Spread over the three runs: osu p95 1.98–1.99, irc 1.59–1.69, api 2.83–2.90, CPU 11.8–13.1 %. One run shed 8 sign-ins while the ramp held the CPU near 0.9: sign-in cost (bcrypt, presence to everyone) is the first candidate. |

## Known limits

**Sign-in rate.** A sign-in checks the password with bcrypt (about 145 ms of CPU) and sends the new player everyone's
presence. On 4 cores, 270 sign-ins in 20 s (13.5 a second) held the CPU near 0.9 and the server shed some of them with
503; the osu! client retries. bcrypt is slow on purpose and stays as it is (owner, 2026-11-25). The fixed scenario ramps
over 60 s for that reason.
