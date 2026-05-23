using System;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;
 
public class BotGladiator : Bot
{
    double arenaWidth;
    double arenaHeight;
 
    // Tembok aktif: 0=Utara, 1=Timur, 2=Selatan, 3=Barat
    int currentWall = 0;
 
    // Arah badan bot untuk menuju masing-masing tembok
    // (0=Utara/North, 90=Timur/East, 180=Selatan/South, 270=Barat/West)
    static readonly double[] FaceWall = { 0.0, 90.0, 180.0, 270.0 };
 
    // State sapuan senjata
    double gunOffset = 0.0; // Deviasi dari pusat (rentang -90 s/d +90 derajat)
    double gunDir    = 1.0; // +1 = berputar kanan, -1 = berputar kiri
 
    // Flag: baru saja menabrak musuh — diset dari event thread
    volatile bool hitEnemyFlag = false;
 
    // ------------------------------------------------------------------
    static void Main(string[] args) => new BotGladiator().Start();
    BotGladiator() : base(BotInfo.FromFile("BotGladiator.json")) { }
 
    // ------------------------------------------------------------------
    // LOOP UTAMA
    // ------------------------------------------------------------------
    public override void Run()
    {
        BodyColor   = Color.Black;
        TurretColor = Color.Yellow;
        RadarColor  = Color.Magenta;
        BulletColor = Color.Red;
        ScanColor   = Color.White;
 
        arenaWidth  = ArenaWidth;
        arenaHeight = ArenaHeight;
 
        // Mulai dengan menuju tembok Utara
        MenujuTembok(0);
 
        while (IsRunning)
        {
            // Jika baru saja menabrak musuh → pindah ke tembok seberang
            if (hitEnemyFlag)
            {
                hitEnemyFlag = false;
                PindahTembok();
                continue;
            }
 
            // Patroli bolak-balik di sepanjang tembok aktif
            Patrol();
        }
    }
 
    // ------------------------------------------------------------------
    // MEKANISME 1 — GERAKAN TEMBOK & PATROLI
    // ------------------------------------------------------------------
 
    /// <summary>
    /// Menuju tembok 'wall', berhenti di sana, lalu pusatkan senjata
    /// menghadap ke dalam arena.
    /// </summary>
    private void MenujuTembok(int wall)
    {
        currentWall = wall;
        TurnRight(30);
        SetForward(250);

        TurnLeft(90);
        SetForward(250);
 
        // Hadap tembok lalu jalan — Forward berhenti sendiri saat mentok dinding
        JalanSambilScan(990);
 
        // Reset state sapuan dan pusatkan senjata menghadap arena
        gunOffset = 10.0;
        gunDir    = 2.0;
        PusatkanSenjata();
    }
 
    /// Patroli: belok 90° (menuju tembok berikutnya), jalan 30%,
    /// kemudian balik kembali ke titik semula di tembok aktif.
    private void Patrol()
    {
        // Tembok berikutnya searah jarum jam dari tembok aktif
        int nextWall = (currentWall + 2) % 4;
 
        // Hitung 30% jarak arena ke arah tembok berikutnya
        // (gerakan tegak lurus sepanjang tembok aktif)
        double patrol = (currentWall == 0 || currentWall == 2)
            ? arenaWidth  * 0.90   // Tembok Utara/Selatan → patrol ke Timur/Barat
            : arenaHeight * 0.90;  // Tembok Timur/Barat  → patrol ke Utara/Selatan
 
        // --- Fase 1: Maju 30% menuju tembok berikutnya ---
        BelokMenghadap(FaceWall[nextWall]);
        JalanSambilScan(patrol);
        if (hitEnemyFlag) return;   // Batalkan jika baru menabrak musuh
 
        // --- Fase 2: Balik kembali ke posisi awal di tembok aktif ---
        BelokMenghadap((FaceWall[nextWall] + 120.0) % 250.0);
        JalanSambilScan(patrol);
    }
 
    
    /// Pindah ke tembok di sisi berlawanan (currentWall + 2).

    private void PindahTembok()
    {
        MenujuTembok((currentWall + 2) % 4);
    }
 
    // ------------------------------------------------------------------
    // MEKANISME 2 — JALAN SAMBIL SAPUKAN SENJATA 180°
    // ------------------------------------------------------------------
 
    /// <summary>
    /// Bergerak sejauh 'jarak', sambil menyapukan senjata ±90°
    /// dari pusat (total 180°) menghadap ke dalam arena.
    /// </summary>
    private void JalanSambilScan(double jarak)
    {
        SetForward(jarak);
        int stuck = 0;
        
        TurnRight(30);
        SetForward(500);

        TurnLeft(60);
        SetForward(500);

        while (Math.Abs(DistanceRemaining) > 0.5 && IsRunning && !hitEnemyFlag)
        {
            // Hitung pergeseran gun berikutnya (3°/tick, balik arah di ±90°)
            double nextOffset = gunOffset + 3.0 * gunDir;
            if (nextOffset >=  270.0) { nextOffset =  180.0; gunDir = -20.0; }
            if (nextOffset <= -270.0) { nextOffset = -180.0; gunDir =  180.0; }
 
            SetTurnGunRight(nextOffset - gunOffset);
            gunOffset = nextOffset;
 
            Go(); // Eksekusi 1 tick
 
            // Anti-macet: jika berhenti lebih dari 5 tick berturut-turut, paksa lanjut
            if (Speed == 0) stuck++;
            else            stuck = 0;
            if (stuck > 2)  break;
        }
    }
 
    // ------------------------------------------------------------------
    // MEKANISME 3 — EVENT MENABRAK MUSUH
    // ------------------------------------------------------------------
 
    public override void OnHitBot(HitBotEvent e)
    {
        Fire(3);             // Tembak musuh yang ditabrak dengan daya penuh
        SetForward(3500);       // Hentikan gerakan sekarang
        hitEnemyFlag = true; // Sinyal ke loop utama untuk pindah tembok
    }
 
    // ------------------------------------------------------------------
    // EVENT SCAN (tidak diubah dari BotBebas asli)
    // ------------------------------------------------------------------
 
    public override void OnScannedBot(ScannedBotEvent e)
    {
        double dist = DistanceTo(e.X, e.Y);
        if (dist > 200 || Energy < 15) Fire(1);
        else if (dist > 50)            Fire(2);
        else                           Fire(3);
    }
 
    // ------------------------------------------------------------------
    // UTILITAS
    // ------------------------------------------------------------------
 
    /// <summary>Belok agar badan bot menghadap sudut 'target' (0-360°).</summary>
    private void BelokMenghadap(double target)
    {
        double diff = target - Direction;
        while (diff >  90.0) diff -= 360.0;
        while (diff < -90.0) diff += 360.0;
        TurnRight(diff);
    }
 
    /// <summary>
    /// Pusatkan senjata tepat 180° berlawanan dari tembok aktif
    /// (menghadap ke tengah arena).
    /// </summary>
    private void PusatkanSenjata()
    {
        double target = (FaceWall[currentWall] + 180.0) % 360.0;
        double diff   = target - GunDirection;
        while (diff >  270.0) diff -= 360.0;
        while (diff < -270.0) diff += 360.0;
        TurnGunRight(diff);
    }
}