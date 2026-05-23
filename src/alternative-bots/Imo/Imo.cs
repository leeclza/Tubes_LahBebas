using System;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class Imo : Bot
{
    // ═══════════════════════════════════════════════
    // WALL PATROL STATE
    // Strategi Greedy: selalu pilih posisi tembok yang
    // memberikan coverage arena terluas saat ini
    // ═══════════════════════════════════════════════
    int currentWall = 0; // 0=Utara, 1=Timur, 2=Selatan, 3=Barat

    // Arah hadap badan untuk tiap tembok
    static readonly double[] FaceWall = { 0.0, 90.0, 180.0, 270.0 };

    // State sapuan senjata (±90° dari pusat = 180° total)
    double gunOffset = 0.0;
    double gunDir    = 1.0; // +1=kanan, -1=kiri

    // Flag tabrakan musuh (dari event thread)
    volatile bool hitEnemyFlag = false;

    // ═══════════════════════════════════════════════
    // ENTRY POINT
    // ═══════════════════════════════════════════════
    static void Main(string[] args) => new Imo().Start();
    Imo() : base(BotInfo.FromFile("Imo.json")) { }

    // ═══════════════════════════════════════════════
    // MAIN LOOP
    // Greedy: setiap tick pilih aksi terbaik berdasarkan
    // kondisi saat ini (patrol vs pindah tembok)
    // ═══════════════════════════════════════════════
    public override void Run()
    {
        BodyColor   = Color.FromArgb(0x1B, 0x1B, 0x2F);
        TurretColor = Color.FromArgb(0xF5, 0xA6, 0x23);
        RadarColor  = Color.FromArgb(0xFF, 0x00, 0xFF);
        BulletColor = Color.FromArgb(0xFF, 0x22, 0x22);
        ScanColor   = Color.FromArgb(0xFF, 0xFF, 0xFF);
        TracksColor = Color.FromArgb(0x0A, 0x0A, 0x1A);
        GunColor    = Color.FromArgb(0xF5, 0xA6, 0x23);

        // Greedy init: langsung ke tembok terdekat (Utara)
        GoToWall(0);

        while (IsRunning)
        {
            // Greedy decision: kalau baru nabrak musuh → pindah tembok seberang
            // karena musuh kemungkinan di area yang sama
            if (hitEnemyFlag)
            {
                hitEnemyFlag = false;
                GoToWall((currentWall + 2) % 4);
                continue;
            }

            // Default: patroli sepanjang tembok aktif
            Patrol();
        }
    }

    // ═══════════════════════════════════════════════
    // WALL NAVIGATION
    // Greedy: pilih tembok → langsung jalan ke sana
    // tanpa evaluasi posisi lain
    // ═══════════════════════════════════════════════
    void GoToWall(int wall)
    {
        currentWall = wall;

        // Hadap tembok → jalan sampai mentok
        FaceDirection(FaceWall[wall]);
        WalkAndSweep(5000);

        // Reset gun ke tengah arena setelah sampai
        gunOffset = 0.0;
        gunDir    = 1.0;
        CenterGun();
    }

    // ═══════════════════════════════════════════════
    // PATROL
    // Greedy: selalu gerak 30% lebar arena bolak-balik
    // Heuristic: coverage maksimal dengan jarak minimal
    // ═══════════════════════════════════════════════
    void Patrol()
    {
        int nextWall = (currentWall + 1) % 4;

        // Jarak patrol: 30% dimensi arena ke arah tembok berikutnya
        double patrolDist = (currentWall == 0 || currentWall == 2)
            ? ArenaWidth  * 0.30   // Utara/Selatan → patrol horizontal
            : ArenaHeight * 0.30;  // Timur/Barat   → patrol vertikal

        // Fase 1: maju 30% ke arah tembok berikutnya
        FaceDirection(FaceWall[nextWall]);
        WalkAndSweep(patrolDist);
        if (hitEnemyFlag) return;

        // Fase 2: balik ke posisi awal
        FaceDirection((FaceWall[nextWall] + 180.0) % 360.0);
        WalkAndSweep(patrolDist);
    }

    // ═══════════════════════════════════════════════
    // WALK AND GUN SWEEP
    // Jalan sambil sapukan gun ±90° (total 180°)
    // Greedy: tembak langsung saat scan mendeteksi musuh
    // ═══════════════════════════════════════════════
    void WalkAndSweep(double distance)
    {
        SetForward(distance);
        int stuckTick = 0;

        while (Math.Abs(DistanceRemaining) > 0.5 && IsRunning && !hitEnemyFlag)
        {
            // Hitung posisi gun berikutnya (3° per tick)
            double nextOffset = gunOffset + 3.0 * gunDir;

            // Balik arah sapuan di batas ±90°
            if (nextOffset >=  90.0) { nextOffset =  90.0; gunDir = -1.0; }
            if (nextOffset <= -90.0) { nextOffset = -90.0; gunDir =  1.0; }

            SetTurnGunRight(nextOffset - gunOffset);
            gunOffset = nextOffset;

            Go();

            // Anti-stuck: kalau diam lebih dari 5 tick → paksa lanjut
            stuckTick = (Speed == 0) ? stuckTick + 1 : 0;
            if (stuckTick > 5) break;
        }
    }

    // ═══════════════════════════════════════════════
    // HELPER: rotasi badan ke arah tertentu
    // ═══════════════════════════════════════════════
    void FaceDirection(double targetAngle)
    {
        double diff = targetAngle - Direction;
        while (diff >  180.0) diff -= 360.0;
        while (diff < -180.0) diff += 360.0;
        TurnRight(diff);
    }

    // ═══════════════════════════════════════════════
    // HELPER: pusatkan gun menghadap tengah arena
    // ═══════════════════════════════════════════════
    void CenterGun()
    {
        double target = (FaceWall[currentWall] + 180.0) % 360.0;
        double diff   = target - GunDirection;
        while (diff >  180.0) diff -= 360.0;
        while (diff < -180.0) diff += 360.0;
        TurnGunRight(diff);
    }

    // ═══════════════════════════════════════════════
    // EVENTS
    // ═══════════════════════════════════════════════

    // Greedy fire: tembak sekeras mungkin sesuai jarak
    public override void OnScannedBot(ScannedBotEvent e)
    {
        double dist = DistanceTo(e.X, e.Y);

        // Greedy firepower selection berdasarkan jarak
        if (Energy < 15)    Fire(1);        // hemat energi kalau kritis
        else if (dist > 200) Fire(1);       // jauh → peluru ringan, cepat
        else if (dist > 50)  Fire(2);       // mid  → seimbang
        else                 Fire(3);       // dekat → maksimal
    }

    // Nabrak musuh → tembak + sinyal pindah tembok
    public override void OnHitBot(HitBotEvent e)
    {
        Fire(3);             // tembak langsung dengan daya penuh
        SetForward(0);       // stop gerakan
        hitEnemyFlag = true; // sinyal loop utama
    }

    public override void OnHitWall(HitWallEvent e)
    {
        // Kena tembok → balik arah sedikit
        TurnRight(10);
    }

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        // Kena peluru → sedikit dodge
        TurnRight(30);
        Forward(50);
    }

    public override void OnDeath(DeathEvent e)
    {
        Console.WriteLine($"[Imo] Mati di turn {TurnNumber}.");
    }
}