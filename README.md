# Tubes_LahBebas

Tugas Besar IF25-21013 Strategi Algoritma — Semester Genap 2026/2027  
Kelompok 8 RC — Program Studi Teknik Informatika, Institut Teknologi Sumatera

---

## Deskripsi Singkat

Repository ini berisi 4 bot Robocode Tank Royale yang masing-masing mengimplementasikan algoritma Greedy dengan heuristik berbeda.

---

## Algoritma Greedy per Bot

### 1. LahBebas *(Bot Utama)*
**Strategi: Adaptive State Machine Greedy**

Bot menggunakan Finite State Machine (FSM) dengan 5 state: HUNT, FIGHT, FLEE, DODGE, dan UNSTUCK. Setiap turn, bot mengevaluasi kondisi secara greedy — memilih state dengan prioritas tertinggi yang kondisinya terpenuhi saat itu.

Heuristik utama:
- **Target selection:** `score = jarak + (staleness × 50)` → pilih musuh dengan score terkecil
- **Firepower:** threshold jarak → `<150: 3.0 | <300: 2.0 | <500: 1.5 | else: 1.0`
- **Movement:** strafe perpendicular terhadap musuh di state FIGHT
- **Fire timing:** tembak jika `|gunTurn| < 5°`, atau force-fire jika 30 turn tidak tembak

---

### 2. BotBebas *(Bot Alternatif 1)*
**Strategi: Wall Patrol Greedy — Gerakan Segitiga di Tembok**

Bot bergerak ke tembok terdekat lalu berpatroli bolak-balik sepanjang 30% dimensi arena. Selama bergerak, gun disapukan ±90° (total 180°) menghadap ke dalam arena.

Heuristik utama:
- **Tembok tujuan:** Utara (default), pindah ke seberang `(currentWall + 2)` saat menabrak musuh
- **Patrol:** 30% lebar/tinggi arena, searah jarum jam
- **Firepower:** `dist > 200 → Fire(1) | dist > 50 → Fire(2) | else → Fire(3)`
- **Gun sweep:** 3°/tick, balik arah di batas ±90°

---

### 3. BotGladiator *(Bot Alternatif 2)*
**Strategi: Aggressive Wall Patrol — Modified Gerakan Segitiga**

Varian agresif dari BotBebas. Patrol mencakup 90% dimensi arena dengan tambahan zigzag sebelum gun sweep dimulai untuk memperluas coverage dan mempersulit prediksi posisi bot.

Heuristik utama:
- **Patrol:** 90% dimensi arena (vs 30% BotBebas), pindah ke tembok seberang `(currentWall + 2)`
- **Zigzag awal:** `TurnRight(30) → Forward(250) → TurnLeft(90) → Forward(250)`
- **Zigzag dalam:** `TurnRight(30) → Forward(500) → TurnLeft(60) → Forward(500)`
- **Gun sweep:** 3°/tick dengan batas ±270° (lebih lebar dari BotBebas)
- **Firepower:** sama seperti BotBebas (berbasis jarak)

---

### 4. Imo *(Bot Alternatif 3)*
**Strategi: Wall Patrol Greedy Defensif**

Secara struktural mirip BotBebas (patrol 30%), namun dengan tambahan greedy firepower yang mempertimbangkan kondisi energi diri sendiri selain jarak musuh — selamatkan diri dulu sebelum menyerang.

Heuristik utama:
- **Firepower:** cek energi diri dulu → `Energy < 15 → Fire(1)`, lalu `dist > 200 → Fire(1) | dist > 50 → Fire(2) | else → Fire(3)`
- **Dodge:** `OnHitByBullet → TurnRight(30) + Forward(50)` (reaktif)
- **Wall correction:** `OnHitWall → TurnRight(10)`
- **Pindah tembok:** `hitEnemyFlag == true → (currentWall + 2) % 4`

---

## Requirements

| Dependency | Versi |
|---|---|
| Java (untuk game engine) | ≥ 11 (direkomendasikan Temurin 25) |
| .NET SDK | ≥ 6.0 (direkomendasikan .NET 10) |
| Robocode Tank Royale BotApi | 0.30.0 (sudah tercantum di `.csproj`) |

### Instalasi Java
Download dan install dari: https://adoptium.net

### Instalasi .NET
Download dan install dari: https://dotnet.microsoft.com/download

Verifikasi instalasi:
```bash

## Struktur Repository

```
Tubes_LahBebas/
├── src/
│   ├── main-bot/
│   │   └── LahBebas/
│   │       ├── LahBebas.cs
│   │       ├── LahBebas.csproj
│   │       ├── LahBebas.json
│   │       ├── LahBebas.cmd
│   │       └── LahBebas.sh
│   └── alternative-bots/
│       ├── BotBebas/
│       │   ├── BotBebas.cs
│       │   ├── BotBebas.csproj
│       │   ├── BotBebas.json
│       │   ├── BotBebas.cmd
│       │   └── BotBebas.sh
│       ├── BotGladiator/
│       │   ├── BotGladiator.cs
│       │   ├── BotGladiator.csproj
│       │   ├── BotGladiator.json
│       │   ├── BotGladiator.cmd
│       │   └── BotGladiator.sh
│       └── Imo/
│           ├── Imo.cs
│           ├── Imo.csproj
│           ├── Imo.json
│           ├── Imo.cmd
│           └── Imo.sh
├── doc/
│   └── Lah Bebas.pdf
└── README.md
```

---

## Author

| Nama | NIM | Kontribusi |
|---|---|---|
| Christoper Leon Saputra | 124140097 | LahBebas (bot utama), Imo (alt-bot-3) |
| Galih Sigit Satrio | 124140001 | BotBebas (alt-bot-1) |
| Kenzie Sahasika Tariana | 124140103 | BotGladiator (alt-bot-2) |

**Kelompok 8 RC — IF25-21013 Strategi Algoritma**  
Program Studi Teknik Informatika  
Institut Teknologi Sumatera  
Semester Genap 2026/2027