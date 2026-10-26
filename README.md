# Nexus Saga — Backend (Meta)

ASP.NET Core 8 API untuk **Nexus Saga: Chronicles** — meta game Solo 1v1 Ranked:
guest auth (device→JWT), profil, **matchmaking MMR**, **rating Glicko-2**, leaderboard,
report-result `kedua pemain lapor & harus cocok` (sim deterministik → semi anti-cheat),
Photon custom-auth + webhook.

## Jalankan (dev, cukup .NET 8 SDK — tanpa Docker)
```
cd backend
dotnet test                                                   # unit + integration test
dotnet run --project src/PhotonBackend.Api --urls http://localhost:5080
```
Mode Development pakai DB in-memory (`Database:UseInMemory=true`).

## Produksi (Docker: Postgres + API [+ Caddy TLS])
```
cp .env.example .env    # isi POSTGRES_PASSWORD, JWT_SECRET, Photon secrets
docker compose up -d --build
```

## Endpoint utama
- `POST /api/auth/guest` · `GET /api/me` · `GET /api/leaderboard`
- `POST /api/matchmaking/join` · `GET /api/matchmaking/status` · `POST /api/matchmaking/leave`
- `POST /api/matches/result` · `GET /api/matches`
- `POST /photon/auth` · `POST /photon/webhooks/...` (integrasi Photon)
