using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class LahBebas : Bot
{
    // ═══════════════════════════════════════════════
    // ENEMY TRACKER
    // ═══════════════════════════════════════════════
    class EnemyInfo
    {
        public int Id;
        public double X, Y;
        public double PrevX, PrevY;
        public double Direction;
        public double Speed;
        public double Energy;
        public double PrevEnergy = 100;
        public long LastSeen;

        public double VelocityX => X - PrevX;
        public double VelocityY => Y - PrevY;
        public bool JustFired => (PrevEnergy - Energy) is > 0.09 and < 3.1;
        public bool IsAlive(long currentTurn) => currentTurn - LastSeen < 30;
    }

    // ═══════════════════════════════════════════════
    // STATE MACHINE
    // ═══════════════════════════════════════════════
    enum BotState { HUNT, FIGHT, FLEE, DODGE, UNSTUCK }

    // ═══════════════════════════════════════════════
    // FIELDS
    // ═══════════════════════════════════════════════
    readonly Dictionary<int, EnemyInfo> enemies = new();
    EnemyInfo nearestEnemy = null;

    BotState currentState  = BotState.HUNT;
    int moveDirection      = 1;
    int moveTurnCounter    = 0;
    double moveDistance    = 100;
    bool dodging           = false;
    int dodgeTurns         = 0;

    // ── Anti-stuck ──
    double prevX = -1, prevY = -1;
    int stuckCounter        = 0;
    const int STUCK_THRESHOLD = 3;  
    const double STUCK_DIST   = 5.0;
    bool isUnstucking       = false;
    int unstuckTurns        = 0;
    int unstuckPhase        = 0;
    int wallCooldown        = 0;
    const double WALL_MARGIN  = 100.0;

    // ── State timeout / anti-deadlock ──
    BotState lastState         = BotState.HUNT;
    int stateHoldTurns         = 0;
    const int STATE_TIMEOUT    = 40;  

    // ── Anti rotate-loop ──
    double lastGunDirection    = 0;
    int rotateLoopCounter      = 0;
    const int ROTATE_LOOP_MAX  = 20; 

    // ── Combat override ──
    bool combatOverride        = false;
    int combatOverrideTurns    = 0;
    const int COMBAT_OVERRIDE_DURATION = 15;

    // ── Fire tracking ──
    int turnsSinceLastFire     = 0;
    const int FORCE_FIRE_AFTER = 30;  

    readonly Random rng = new();

    // ═══════════════════════════════════════════════
    // ENTRY POINT
    // ═══════════════════════════════════════════════
    static void Main(string[] args) => new LahBebas().Start();
    LahBebas() : base(BotInfo.FromFile("LahBebas.json")) { }

    // ═══════════════════════════════════════════════
    // MAIN LOOP
    // ═══════════════════════════════════════════════
    public override void Run()
    {
        BodyColor   = Color.FromArgb(0x1A, 0x1A, 0x2E);
        TurretColor = Color.FromArgb(0xE9, 0x4F, 0x37);
        RadarColor  = Color.FromArgb(0x00, 0xFF, 0xC8);
        BulletColor = Color.FromArgb(0xFF, 0xD7, 0x00);
        ScanColor   = Color.FromArgb(0x00, 0xFF, 0xC8);
        TracksColor = Color.FromArgb(0x11, 0x11, 0x22);
        GunColor    = Color.FromArgb(0xE9, 0x4F, 0x37);

        AdjustGunForBodyTurn   = true;
        AdjustRadarForGunTurn  = true;
        AdjustRadarForBodyTurn = true;

        while (IsRunning)
        {
            // ── Tick semua sistem ──
            UpdateStuckDetection();
            UpdateStateTimeout();
            UpdateCombatOverride();
            if (wallCooldown > 0) wallCooldown--;
            turnsSinceLastFire++;

            // ── Update context ──
            nearestEnemy = GetNearestEnemy();
            currentState = EvaluateState();

            // ── Radar: SELALU independen ──
            RadarLogic();

            // ── Gun tracking──
            GunTrackingLogic();

            // ── Movement ──
            switch (currentState)
            {
                case BotState.UNSTUCK: UnstuckRoutine();  break;
                case BotState.FIGHT:   FightMovement();   break;
                case BotState.FLEE:    FleeMovement();    break;
                case BotState.DODGE:   DodgeMovement();   break;
                default:               HuntMovement();    break;
            }

            // ── Fire ──
            FireLogic();

            Go();

            prevX = X;
            prevY = Y;
        }
    }

    // ═══════════════════════════════════════════════
    // ANTI-DEADLOCK: STATE TIMEOUT
    // Kalau terlalu lama di satu state → force reset
    // ═══════════════════════════════════════════════
    void UpdateStateTimeout()
    {
        if (currentState == lastState)
        {
            stateHoldTurns++;
            if (stateHoldTurns >= STATE_TIMEOUT)
            {
                // Force break deadlock
                stateHoldTurns = 0;
                dodging        = false;
                dodgeTurns     = 0;

                // stuck kelamaan = spiral
                if (currentState == BotState.HUNT)
                {
                    SetTurnRight(rng.Next(30, 90));
                    SetForward(200);
                }
            }
        }
        else
        {
            stateHoldTurns = 0;
            lastState      = currentState;
        }
    }

    // ═══════════════════════════════════════════════
    // COMBAT OVERRIDE TICKER
    // ═══════════════════════════════════════════════
    void UpdateCombatOverride()
    {
        if (combatOverride)
        {
            combatOverrideTurns--;
            if (combatOverrideTurns <= 0)
            {
                combatOverride      = false;
                combatOverrideTurns = 0;
            }
        }
    }

    // ═══════════════════════════════════════════════
    // ANTI-STUCK DETECTION
    // ═══════════════════════════════════════════════
    void UpdateStuckDetection()
    {
        if (prevX < 0) return;

        double moved = Math.Sqrt(
            Math.Pow(X - prevX, 2) + Math.Pow(Y - prevY, 2));

        if (moved < STUCK_DIST && !isUnstucking)
        {
            stuckCounter++;
            if (stuckCounter >= STUCK_THRESHOLD)
                TriggerUnstuck();
        }
        else if (!isUnstucking)
        {
            stuckCounter = 0;
        }
    }

    void TriggerUnstuck()
    {
        isUnstucking = true;
        unstuckPhase = 0;
        unstuckTurns = 0;
        stuckCounter = 0;
    }

    // ═══════════════════════════════════════════════
    // STATE EVALUATOR
    // ═══════════════════════════════════════════════
    BotState EvaluateState()
    {
        // Combat override: paksa FIGHT walau kondisi buruk
        if (combatOverride && nearestEnemy != null)
            return BotState.FIGHT;

        // Unstuck: prioritas tinggi tapi tidak block fire
        if (isUnstucking) return BotState.UNSTUCK;

        // Dodge
        if (dodging && dodgeTurns > 0)
        {
            dodgeTurns--;
            if (dodgeTurns == 0) dodging = false;
            return BotState.DODGE;
        }

        // Flee
        if (Energy < 15) return BotState.FLEE;

        // Fight
        if (nearestEnemy != null && DistanceTo(nearestEnemy) < 600)
            return BotState.FIGHT;

        return BotState.HUNT;
    }

    // ═══════════════════════════════════════════════
    // UNSTUCK ROUTINE
    // ═══════════════════════════════════════════════
    void UnstuckRoutine()
    {
        unstuckTurns++;
        switch (unstuckPhase)
        {
            case 0: // mundur lebih cepat
                SetBack(100);          
                SetTurnRight(rng.Next(45, 90) * moveDirection); 
                if (unstuckTurns >= 3) 
                { unstuckPhase = 1; unstuckTurns = 0; }
                break;

            case 1: 
                double angleToCenter = BearingToTarget(ArenaWidth / 2.0, ArenaHeight / 2.0);
                SetTurnRight(NormAngle(angleToCenter + rng.Next(-45, 45)));
                if (unstuckTurns >= 6) { unstuckPhase = 2; unstuckTurns = 0; }
                break;

            case 2: 
                SetForward(150);
                if (unstuckTurns >= 8)
                {
                    isUnstucking = false;
                    unstuckPhase = 0;
                    unstuckTurns = 0;
                    wallCooldown = 10;
                    if (nearestEnemy != null)
                    {
                        combatOverride      = true;
                        combatOverrideTurns = COMBAT_OVERRIDE_DURATION;
                    }
                }
                break;
        }
    }

    // ═══════════════════════════════════════════════
    // RADAR LOGIC
    // ═══════════════════════════════════════════════
    void RadarLogic()
{
    bool enemyRecentlySeen = nearestEnemy != null
                             && TurnNumber - nearestEnemy.LastSeen < 5;

    if (enemyRecentlySeen)
    {
        // Selalu lock ke nearestEnemy
        double angleToEnemy = DirectionTo(nearestEnemy.X, nearestEnemy.Y);
        double radarTurn    = NormAngle(angleToEnemy - RadarDirection);
        radarTurn += Math.Sign(radarTurn) * 5;
        SetTurnRadarLeft(radarTurn);
    }
    else
    {
        // Musuh ga keliatan = sweep pelan 
        SetTurnRadarRight(45);
    }
}

    // ═══════════════════════════════════════════════
    // GUN TRACKING (independen dari movement/state)
    // Selalu putar gun ke prediksi posisi musuh
    // ═══════════════════════════════════════════════
    void GunTrackingLogic()
{
    if (nearestEnemy == null) return;

    // Kalau musuh sudah lama tidak keliatan jangan aim prediksi
    bool recentlySeen = TurnNumber - nearestEnemy.LastSeen < 8;

    double angleToTarget;
    if (recentlySeen)
    {
        double firePower   = SelectFirePower(nearestEnemy, DistanceTo(nearestEnemy));
        var (predX, predY) = PredictEnemyPosition(nearestEnemy, firePower);
        angleToTarget      = DirectionTo(predX, predY);
    }
    else
    {
        // Aim langsung ke posisi terakhir musuh
        angleToTarget = DirectionTo(nearestEnemy.X, nearestEnemy.Y);
    }

    double gunTurn = NormAngle(angleToTarget - GunDirection);

    // Anti rotate-loop
    double gunDelta = Math.Abs(NormAngle(GunDirection - lastGunDirection));
    if (gunDelta > 1.0)
    {
        rotateLoopCounter++;
        if (rotateLoopCounter >= ROTATE_LOOP_MAX)
        {
            gunTurn           = NormAngle(DirectionTo(nearestEnemy.X, nearestEnemy.Y) - GunDirection);
            rotateLoopCounter = 0;
        }
    }
    else
    {
        rotateLoopCounter = 0;
    }

    lastGunDirection = GunDirection;
    SetTurnGunLeft(gunTurn);
}

    // ═══════════════════════════════════════════════
    // MOVEMENT
    // ═══════════════════════════════════════════════
    void HuntMovement()
    {
        SmartAvoidWalls();
        SetTurnRight(15);
        SetForward(150);
    }

    void FightMovement()
    {
        if (nearestEnemy == null) return;

        moveTurnCounter++;
        if (moveTurnCounter >= rng.Next(12, 28))
        {
            moveDirection   *= -1;
            moveTurnCounter  = 0;
            moveDistance     = rng.Next(70, 160);
        }

        double bearingToEnemy    = BearingToTarget(nearestEnemy.X, nearestEnemy.Y);
        double perpendicularAngle = bearingToEnemy + 90;
        SetTurnRight(NormAngle(perpendicularAngle));
        SetForward(moveDistance * moveDirection);

        SmartAvoidWalls();
    }

    void FleeMovement()
    {
        if (nearestEnemy == null) { HuntMovement(); return; }

        double[] cornersX = { 80, ArenaWidth - 80, 80, ArenaWidth - 80 };
        double[] cornersY = { 80, 80, ArenaHeight - 80, ArenaHeight - 80 };

        int bestCorner = 0; double bestDist = 0;
        for (int i = 0; i < 4; i++)
        {
            double d = Math.Sqrt(
                Math.Pow(cornersX[i] - nearestEnemy.X, 2) +
                Math.Pow(cornersY[i] - nearestEnemy.Y, 2));
            if (d > bestDist) { bestDist = d; bestCorner = i; }
        }

        SetTurnRight(NormAngle(BearingToTarget(cornersX[bestCorner], cornersY[bestCorner])));
        SetForward(200);
        SmartAvoidWalls();
    }

    void DodgeMovement()
    {
        moveDirection *= -1;
        SetTurnRight(rng.Next(60, 120) * moveDirection);
        SetForward(100 * moveDirection);
        SmartAvoidWalls();
    }

    // ═══════════════════════════════════════════════
    // SMART WALL AVOIDANCE (smooth, anti-jitter)
    // ═══════════════════════════════════════════════
    void SmartAvoidWalls()
    {
        if (wallCooldown > 0) return;

        bool nearLeft   = X < WALL_MARGIN;
        bool nearRight  = X > ArenaWidth  - WALL_MARGIN;
        bool nearBottom = Y < WALL_MARGIN;
        bool nearTop    = Y > ArenaHeight - WALL_MARGIN;

        if (!nearLeft && !nearRight && !nearBottom && !nearTop) return;

        double targetX = Math.Clamp(X, WALL_MARGIN * 1.5, ArenaWidth  - WALL_MARGIN * 1.5);
        double targetY = Math.Clamp(Y, WALL_MARGIN * 1.5, ArenaHeight - WALL_MARGIN * 1.5);

        int wallCount = (nearLeft ? 1 : 0) + (nearRight  ? 1 : 0) +
                        (nearBottom ? 1 : 0) + (nearTop  ? 1 : 0);
        if (wallCount >= 2)
        {
            targetX = ArenaWidth  / 2.0;
            targetY = ArenaHeight / 2.0;
        }

        double escapeAngle = BearingToTarget(targetX, targetY);
        if (Math.Abs(escapeAngle) > 20)
            SetTurnRight(NormAngle(escapeAngle));

        SetForward(120);
        wallCooldown = 5;
    }

    // ═══════════════════════════════════════════════
    // FIRE LOGIC
    // Independen dari movement state.
    // Selalu dicoba setiap turn.
    // Force fire kalau sudah terlalu lama tidak tembak.
    // ═══════════════════════════════════════════════
    void FireLogic()
    {
        if (nearestEnemy == null) return;
        if (GunHeat > 0) return;

        double distance  = DistanceTo(nearestEnemy);
        double firePower = SelectFirePower(nearestEnemy, distance);
        double gunTurn   = NormAngle(DirectionTo(nearestEnemy.X, nearestEnemy.Y) - GunDirection);

        // Normal fire: gun sudah lurus
        bool gunAligned = Math.Abs(gunTurn) < 5;

        // Force fire: lama ga nembak → sudut lebih besar
        bool forceFire = turnsSinceLastFire >= FORCE_FIRE_AFTER
                         && Math.Abs(gunTurn) < 20;

        if (gunAligned || forceFire)
        {
            SetFire(firePower);
            turnsSinceLastFire = 0;
        }
    }

    double SelectFirePower(EnemyInfo enemy, double distance)
    {
        if (enemy.Energy <= 4) return Math.Min(3.0, enemy.Energy + 0.1);
        if (distance < 150) return 3.0;
        if (distance < 300) return 2.0;
        if (distance < 500) return 1.5;
        return 1.0;
    }

    (double x, double y) PredictEnemyPosition(EnemyInfo enemy, double firePower)
    {
        double bulletSpeed = 20 - (3 * firePower);
        double distance    = DistanceTo(enemy);
        double travelTime  = distance / bulletSpeed;

        for (int i = 0; i < 10; i++)
        {
            double px   = enemy.X + enemy.VelocityX * travelTime;
            double py   = enemy.Y + enemy.VelocityY * travelTime;
            travelTime  = Math.Sqrt(Math.Pow(px - X, 2) + Math.Pow(py - Y, 2)) / bulletSpeed;
        }

        return (
            Math.Clamp(enemy.X + enemy.VelocityX * travelTime, 20, ArenaWidth  - 20),
            Math.Clamp(enemy.Y + enemy.VelocityY * travelTime, 20, ArenaHeight - 20)
        );
    }

    // ═══════════════════════════════════════════════
    // HELPER
    // ═══════════════════════════════════════════════
    static double NormAngle(double angle)
    {
        while (angle >  180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    double BearingToTarget(double tx, double ty) =>
        NormAngle(DirectionTo(tx, ty) - Direction);

    double DistanceTo(EnemyInfo e) =>
        Math.Sqrt(Math.Pow(e.X - X, 2) + Math.Pow(e.Y - Y, 2));

    EnemyInfo GetNearestEnemy()
{
    EnemyInfo best    = null;
    double bestScore  = double.MaxValue;

    foreach (var e in enemies.Values)
    {
        if (TurnNumber - e.LastSeen > 15) continue;

        double dist       = DistanceTo(e);
        long staleness    = TurnNumber - e.LastSeen;

        // Score: kombinasi jarak + staleness
        // Musuh dekat + baru keliatan = score rendah = prioritas tinggi
        double score = dist + (staleness * 50);

        if (score < bestScore) { bestScore = score; best = e; }
    }
    return best;
}

    // ═══════════════════════════════════════════════
    // EVENTS
    // ═══════════════════════════════════════════════

    public override void OnRoundStarted(RoundStartedEvent e)
    {
        // Reset semua state antar ronde
        enemies.Clear();
        nearestEnemy     = null;
        currentState     = BotState.HUNT;
        moveDirection    = 1;
        moveTurnCounter  = 0;
        moveDistance     = 100;
        dodging          = false;
        dodgeTurns       = 0;

        // Anti-stuck reset
        prevX            = -1;
        prevY            = -1;
        stuckCounter     = 0;
        isUnstucking     = false;
        unstuckTurns     = 0;
        unstuckPhase     = 0;
        wallCooldown     = 0;

        // State timeout reset
        lastState        = BotState.HUNT;
        stateHoldTurns   = 0;

        // Anti rotate-loop reset
        lastGunDirection  = 0;
        rotateLoopCounter = 0;

        // Combat override reset
        combatOverride      = false;
        combatOverrideTurns = 0;

        // Fire tracking reset
        turnsSinceLastFire = 0;
    }
    public override void OnScannedBot(ScannedBotEvent e)
    {
        if (!enemies.TryGetValue(e.ScannedBotId, out var info))
        {
            info = new EnemyInfo { Id = e.ScannedBotId };
            enemies[e.ScannedBotId] = info;
        }

        info.PrevX      = info.X == 0 ? e.X : info.X;
        info.PrevY      = info.Y == 0 ? e.Y : info.Y;
        info.X          = e.X;
        info.Y          = e.Y;
        info.Direction  = e.Direction;
        info.Speed      = e.Speed;
        info.PrevEnergy = info.Energy == 0 ? e.Energy : info.Energy;
        info.Energy     = e.Energy;
        info.LastSeen   = TurnNumber;

        if (info.JustFired && !dodging)
        {
            dodging    = true;
            dodgeTurns = rng.Next(4, 8);
        }
    }

    public override void OnBotDeath(BotDeathEvent e)
    {
        enemies.Remove(e.VictimId);
    }

    public override void OnHitBot(HitBotEvent e)
    {
        moveDirection *= -1;
        SetBack(60);
    }

    public override void OnHitWall(HitWallEvent e)
    {
        // Langsung paksa mundur + rotate sekarang juga
        moveDirection *= -1;
        stuckCounter   = 0;

        // Paksa mundur langsung tanpa nunggu unstuck routine
        SetBack(80);
        double angleToCenter = BearingToTarget(ArenaWidth / 2.0, ArenaHeight / 2.0);
        SetTurnRight(NormAngle(angleToCenter + rng.Next(-30, 30)));
        SetForward(120);

        // Reset unstuck state biar ga overlap
        isUnstucking = false;
        unstuckPhase = 0;
        unstuckTurns = 0;
        wallCooldown = 8;
    }

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        dodging    = true;
        dodgeTurns = rng.Next(5, 10);
    }

    public override void OnDeath(DeathEvent e)
    {
        Console.WriteLine($"[LahBebas] Mati di turn {TurnNumber}. GG.");
    }
   
}