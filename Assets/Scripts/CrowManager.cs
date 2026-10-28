using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrates the entire crow level sequence:
///
///   PHASE 1 — Tejimola (Crow 0) circles and dive-bombs the player alone.
///             After 4 successful dives, the summon screech triggers.
///
///   TRANSITION — Crow 0 lets out a loud screech. 30 murder crows fly in
///                from random far-away positions and join the orbit ring.
///
///   PHASE 2 — All 31 crows circle with per-crow varied parameters
///             (radius, height, speed) for a natural, non-repetitive feel.
///             While the player is outdoors, random crows dive-bomb at
///             staggered intervals. Occasionally a crow peels off to perch.
///             Entering a safe zone triggers a burst of frustrated cawing.
///
/// AUDIO MODEL: Three manager-owned AudioSources cover all sound.
/// Assign AudioClips in the Inspector — no AudioSource needed on each crow.
/// </summary>
public class CrowManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Singleton

    public static CrowManager Instance { get; private set; }

    private void Awake()
    {
        // Proper singleton guard — destroy any duplicate
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Auto-find the player if not assigned
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector Fields

    public enum CrowPhase { Phase1_TejimolaSolo, Transition, Phase2_Murder }

    [Header("Phase (Read-Only in Play)")]
    [SerializeField] private CrowPhase currentPhase = CrowPhase.Phase1_TejimolaSolo;
    public CrowPhase CurrentPhase => currentPhase;

    // ── References ────────────────────────────────────────────────────────

    [Header("Player Target")]
    [SerializeField] private Transform player;

    [Header("Crows")]
    [Tooltip("Drag 'crow 0' here — Tejimola's main form.")]
    [SerializeField] private CrowAgent crow0;

    [Tooltip("Drag crow 1 → crow 30 here (30 entries).")]
    [SerializeField] private List<CrowAgent> murderCrows = new List<CrowAgent>();

    // ── Phase 1 ───────────────────────────────────────────────────────────

    [Header("Phase 1 — Tejimola Solo")]
    [Tooltip("How many times Crow 0 must successfully dive before summoning the murder.")]
    [SerializeField] private int divesBeforeSummon = 8;

    [Tooltip("Seconds between Crow 0 dive attempts while the player is outdoors.")]
    [SerializeField] private float phase1AttackInterval = 5f;

    [Tooltip("Grace period (seconds) after the player steps outside before the first dive.")]
    [SerializeField] private float outdoorGracePeriod = 1.5f;

    // ── Phase 2 ───────────────────────────────────────────────────────────

    [Header("Phase 2 — Murder Active")]
    [Tooltip("Minimum seconds between random dive-bomb attacks while outdoors.")]
    [SerializeField] private float minDiveInterval = 2.5f;

    [Tooltip("Maximum seconds between random dive-bomb attacks while outdoors.")]
    [SerializeField] private float maxDiveInterval = 5.5f;

    [Tooltip("How long (seconds) the player must stay inside a safe zone before the murder retreats " +
             "and only Crow 0 remains. The cycle then repeats.")]
    [SerializeField] private float safeZoneRetreatTime = 15f;

    [Tooltip("Probability (0–1) that a crow will perch each time the perch check fires.")]
    [SerializeField] private float perchChance = 0.35f;

    [Tooltip("How often (seconds) the manager considers sending a crow to perch.")]
    [SerializeField] private float perchCheckInterval = 8f;

    // ── Orbit Variation ───────────────────────────────────────────────────

    [Header("Murder Crow Orbit Variation")]
    [Tooltip("Randomised orbit radius range — creates loose layered ring.")]
    [SerializeField] private float minOrbitRadius = 8f;
    [SerializeField] private float maxOrbitRadius = 16f;

    [Tooltip("Randomised orbit height range — some crows fly higher, some lower.")]
    [SerializeField] private float minOrbitHeight = 12f;
    [SerializeField] private float maxOrbitHeight = 20f;

    [Tooltip("Randomised circle speed — crows gradually spread and re-cluster naturally.")]
    [SerializeField] private float minCircleSpeed = 5f;
    [SerializeField] private float maxCircleSpeed = 11f;

    // ── Transition / Spawn ────────────────────────────────────────────────

    [Header("Transition — Murder Spawn")]
    [Tooltip("Minimum distance from the player at which murder crows spawn.")]
    [SerializeField] private float minSpawnDist = 60f;

    [Tooltip("Maximum distance from the player at which murder crows spawn.")]
    [SerializeField] private float maxSpawnDist = 100f;

    [Tooltip("Seconds between each successive crow appearing (stagger effect).")]
    [SerializeField] private float spawnStaggerDelay = 0.12f;

    [Tooltip("Seconds after the summon screech before crows start spawning.")]
    [SerializeField] private float postScreechSpawnDelay = 1.4f;

    // ── Audio — Clips ─────────────────────────────────────────────────────

    [Header("Audio — Clips")]
    [Tooltip("Generic caw sounds (array — one is picked randomly each time).")]
    [SerializeField] private AudioClip[] cawClips;

    [Tooltip("Aggressive screech sounds used when Crow 0 dives or is agitated.")]
    [SerializeField] private AudioClip[] screechClips;

    [Tooltip("The single loud summon screech Crow 0 plays before the murder arrives.")]
    [SerializeField] private AudioClip summonScreechClip;

    [Tooltip("Short caw burst played when the player ducks into a safe zone.\n" +
             "Falls back to cawClips if left empty.")]
    [SerializeField] private AudioClip[] safeZoneReactionClips;

    // ── Audio — Timing ────────────────────────────────────────────────────

    [Header("Audio — Timing")]
    [Tooltip("Crow 0's ambient caw interval range (seconds) during Phase 1.")]
    [SerializeField] private float minCrow0CawInterval = 4f;
    [SerializeField] private float maxCrow0CawInterval = 9f;

    [Tooltip("Ambient flock caw interval range (seconds) during Phase 2.")]
    [SerializeField] private float minFlockCawInterval = 3f;
    [SerializeField] private float maxFlockCawInterval = 8f;

    // ── Audio — Sources ───────────────────────────────────────────────────

    [Header("Audio — Sources (create 3 child AudioSources on this GameObject)")]
    [Tooltip("3-D AudioSource for Crow 0 / Tejimola's voice. Repositioned each call.")]
    [SerializeField] private AudioSource crow0AudioSource;

    [Tooltip("3-D AudioSource for ambient flock caws. Repositioned each call.")]
    [SerializeField] private AudioSource flockAudioSource;

    [Tooltip("AudioSource for the dramatic summon screech — can be louder / fuller reverb.")]
    [SerializeField] private AudioSource summonAudioSource;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Runtime State

    // Safe zone
    private int   safeZoneCount = 0;
    public  bool  IsPlayerSafe  => safeZoneCount > 0;
    private float safeZoneTimer = 0f; // tracks continuous time inside safe zone during Phase 2

    // Phase 1
    private int   crow0DiveCount = 0;
    private float attackTimer;
    private float crow0CawTimer;

    // Phase 2
    private float diveTimer;
    private float perchTimer;
    private float flockCawTimer;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle

    private void Start()
    {
        ValidateSetup();
        InitPhase1();
    }

    private void Update()
    {
        switch (currentPhase)
        {
            case CrowPhase.Phase1_TejimolaSolo: UpdatePhase1(); break;
            case CrowPhase.Phase2_Murder:        UpdatePhase2(); break;
            // Transition is coroutine-driven — nothing to update here
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Phase 1 — Tejimola Solo

    private void InitPhase1()
    {
        currentPhase = CrowPhase.Phase1_TejimolaSolo;

        // ── Activate Crow 0 ──
        if (crow0 != null)
        {
            crow0.gameObject.SetActive(true);
            crow0.SetAsAggressiveCrow(true);   // lower altitude, closer dives
            crow0.SetOrbitCenter(player);
            crow0.SetAngle(0f);
            crow0.SetState(CrowAgent.State.Circling);

            // Unsubscribe first so we never double-subscribe across retreat cycles
            crow0.OnDiveCompleted -= OnCrow0DiveCompleted;
            crow0.OnDiveCompleted += OnCrow0DiveCompleted;
        }

        // ── Keep all murder crows inactive ──
        foreach (var crow in murderCrows)
            if (crow != null) crow.gameObject.SetActive(false);

        attackTimer  = outdoorGracePeriod;
        crow0CawTimer = Random.Range(minCrow0CawInterval, maxCrow0CawInterval);

        Debug.Log("[CrowManager] Phase 1 — Tejimola (Crow 0) is active.");
    }

    private void UpdatePhase1()
    {
        if (player == null || crow0 == null) return;

        // ── Ambient caws ──
        crow0CawTimer -= Time.deltaTime;
        if (crow0CawTimer <= 0f)
        {
            PlayAt(crow0AudioSource, GetRandom(cawClips), crow0.transform.position);
            crow0CawTimer = Random.Range(minCrow0CawInterval, maxCrow0CawInterval);
        }

        // ── Dive attacks (outdoors only) ──
        if (IsPlayerSafe)
        {
            attackTimer = outdoorGracePeriod; // reset grace each time they're safe
            return;
        }

        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f && crow0.CurrentState == CrowAgent.State.Circling)
        {
            crow0.StartDive(player);
            PlayAt(crow0AudioSource, GetRandom(cawClips), crow0.transform.position); // Phase 1: caws only
            attackTimer = phase1AttackInterval;
        }
    }

    /// <summary>Called by Crow 0 each time it finishes a dive and returns to circling.</summary>
    private void OnCrow0DiveCompleted()
    {
        crow0DiveCount++;
        Debug.Log($"[CrowManager] Crow 0 dive #{crow0DiveCount} complete.");

        if (crow0DiveCount >= divesBeforeSummon && currentPhase == CrowPhase.Phase1_TejimolaSolo)
        {
            StartCoroutine(TransitionToMurder());
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Transition

    private IEnumerator TransitionToMurder()
    {
        currentPhase = CrowPhase.Transition;
        Debug.Log("[CrowManager] TRANSITION — Crow 0 is summoning the murder!");

        // Wait for Crow 0 to finish any in-progress pull-up before the screech
        float timeout = 6f;
        while (crow0 != null
               && crow0.CurrentState != CrowAgent.State.Circling
               && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        // ── Loud summon screech ──
        if (summonScreechClip != null && summonAudioSource != null)
        {
            summonAudioSource.transform.position = crow0.transform.position;
            summonAudioSource.PlayOneShot(summonScreechClip);
        }

        yield return new WaitForSeconds(postScreechSpawnDelay);

        // ── Spawn murder crows one by one, staggered ──
        for (int i = 0; i < murderCrows.Count; i++)
        {
            CrowAgent crow = murderCrows[i];
            if (crow == null) continue;

            // Each crow gets its own randomised parameters — the core of the
            // natural "Itachi flock" feel: no two crows orbit identically.
            float radius   = Random.Range(minOrbitRadius,  maxOrbitRadius);
            float height   = Random.Range(minOrbitHeight,  maxOrbitHeight);
            float speed    = Random.Range(minCircleSpeed,  maxCircleSpeed);
            float lookAhd  = Random.Range(0.25f, 0.55f);

            crow.SetOrbitParameters(radius, height, speed, lookAhd);
            crow.SetOrbitCenter(player);
            crow.gameObject.SetActive(true);
            crow.SpawnFarAway(player.position, minSpawnDist, maxSpawnDist, height);

            yield return new WaitForSeconds(spawnStaggerDelay);
        }

        // Give the flock time to fly in and settle into orbits
        yield return new WaitForSeconds(3f);

        InitPhase2();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Phase 2 — The Murder

    private void InitPhase2()
    {
        currentPhase  = CrowPhase.Phase2_Murder;
        diveTimer     = IsPlayerSafe ? outdoorGracePeriod : Random.Range(minDiveInterval, maxDiveInterval);
        perchTimer    = perchCheckInterval;
        flockCawTimer = Random.Range(minFlockCawInterval, maxFlockCawInterval);
        crow0CawTimer = Random.Range(minCrow0CawInterval, maxCrow0CawInterval);

        Debug.Log("[CrowManager] Phase 2 — the murder has arrived.");
    }

    private void UpdatePhase2()
    {
        if (player == null) return;

        // ── Crow 0 screeches periodically ──
        crow0CawTimer -= Time.deltaTime;
        if (crow0CawTimer <= 0f)
        {
            if (crow0 != null)
                PlayAt(crow0AudioSource, GetRandom(screechClips), crow0.transform.position);
            crow0CawTimer = Random.Range(minCrow0CawInterval, maxCrow0CawInterval);
        }

        // ── Flock ambient caws ──
        flockCawTimer -= Time.deltaTime;
        if (flockCawTimer <= 0f)
        {
            PlayFlockCaw();
            flockCawTimer = Random.Range(minFlockCawInterval, maxFlockCawInterval);
        }

        // ── Occasional perching ──
        perchTimer -= Time.deltaTime;
        if (perchTimer <= 0f)
        {
            TrySchedulePerch();
            perchTimer = perchCheckInterval + Random.Range(-2f, 2f);
        }

        // ── Safe zone — track how long the player is hiding ──
        if (IsPlayerSafe)
        {
            diveTimer = outdoorGracePeriod;

            safeZoneTimer += Time.deltaTime;
            if (safeZoneTimer >= safeZoneRetreatTime)
            {
                RetreatMurder();
            }
            return;
        }

        // Player stepped back outside — reset the retreat timer
        safeZoneTimer = 0f;

        diveTimer -= Time.deltaTime;
        if (diveTimer <= 0f)
        {
            LaunchRandomDive();
            diveTimer = Random.Range(minDiveInterval, maxDiveInterval);
        }
    }

    /// <summary>Picks a random circling crow (from any of the 31) and sends it diving.</summary>
    private void LaunchRandomDive()
    {
        // Pool all circling crows
        List<CrowAgent> available = new List<CrowAgent>();
        if (crow0 != null && crow0.CurrentState == CrowAgent.State.Circling)
            available.Add(crow0);
        foreach (var crow in murderCrows)
            if (crow != null && crow.CurrentState == CrowAgent.State.Circling)
                available.Add(crow);

        if (available.Count == 0) return;

        CrowAgent selected = available[Random.Range(0, available.Count)];
        selected.StartDive(player);

        // Crow 0 screeches when it dives; murder crows just caw
        bool isCrow0 = selected == crow0;
        AudioClip clip = GetRandom(isCrow0 ? screechClips : cawClips);
        if (isCrow0)
            PlayAt(crow0AudioSource, clip, selected.transform.position);
        else
            PlayAt(flockAudioSource, clip, selected.transform.position);
    }

    /// <summary>Randomly picks a murder crow to perch — gives a subtle "watching you" moment.</summary>
    private void TrySchedulePerch()
    {
        if (Random.value > perchChance) return;

        List<CrowAgent> circling = new List<CrowAgent>();
        foreach (var crow in murderCrows)
            if (crow != null && crow.CurrentState == CrowAgent.State.Circling)
                circling.Add(crow);

        if (circling.Count == 0) return;

        circling[Random.Range(0, circling.Count)].TryPerch();
    }

    /// <summary>
    /// Called when the player hides in a safe zone long enough.
    /// Each murder crow banks away to a random far point and deactivates itself on arrival.
    /// Crow 0 stays and Phase 1 restarts — dive count resets to 0.
    /// </summary>
    private void RetreatMurder()
    {
        Debug.Log("[CrowManager] Murder is retreating — player hid too long.");

        // Send each active murder crow flying away in a random direction.
        // They deactivate themselves once they reach the horizon — no instant pop.
        foreach (var crow in murderCrows)
        {
            if (crow == null || !crow.isActiveAndEnabled) continue;
            crow.FlyAway(player.position, minSpawnDist, maxSpawnDist);
        }

        // Reset Phase 1 counters and timers
        crow0DiveCount = 0;
        safeZoneTimer  = 0f;
        attackTimer    = outdoorGracePeriod;
        crow0CawTimer  = Random.Range(minCrow0CawInterval, maxCrow0CawInterval);

        // Ensure Crow 0 is back to circling (in case she was mid-dive)
        if (crow0 != null && crow0.CurrentState != CrowAgent.State.Circling)
            crow0.SetState(CrowAgent.State.Circling);

        // Re-subscribe to the dive event safely (prevent double-fire across cycles)
        if (crow0 != null)
        {
            crow0.OnDiveCompleted -= OnCrow0DiveCompleted;
            crow0.OnDiveCompleted += OnCrow0DiveCompleted;
        }

        // Switch phase — murder crows are now flying away in the background
        // and will deactivate themselves; the manager no longer touches them.
        currentPhase = CrowPhase.Phase1_TejimolaSolo;

        Debug.Log("[CrowManager] Phase 1 restored — murder is scattering. Crow 0 is alone.");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Safe Zone

    /// <summary>
    /// Called by SafeZone.cs when the player enters or exits a safe area.
    /// Uses a counter so overlapping safe zones never produce wrong results.
    /// </summary>
    public void SetPlayerSafe(bool isSafe)
    {
        bool wasSafe = IsPlayerSafe;

        if (isSafe)
            safeZoneCount++;
        else
            safeZoneCount = Mathf.Max(0, safeZoneCount - 1);

        // React when the player FIRST becomes safe — frustrated caw burst
        bool justBecameSafe = !wasSafe && IsPlayerSafe;
        if (justBecameSafe && currentPhase == CrowPhase.Phase2_Murder)
            StartCoroutine(SafeZoneReactionBurst());
    }

    private IEnumerator SafeZoneReactionBurst()
    {
        // 2–3 quick caws — played from a random crow's position in the sky
        // so the audio is 3D and sounds like it's coming from outside the safe zone,
        // not from directly above the player's head.
        AudioClip[] reactionPool = (safeZoneReactionClips != null && safeZoneReactionClips.Length > 0)
                                   ? safeZoneReactionClips
                                   : cawClips;

        int burstCount = Random.Range(2, 4);
        for (int i = 0; i < burstCount; i++)
        {
            PlayAt(flockAudioSource, GetRandom(reactionPool), GetRandomActiveCrowPosition());
            yield return new WaitForSeconds(Random.Range(0.15f, 0.45f));
        }
    }

    /// <summary>
    /// Returns a random active crow's world position for use as a 3D audio source.
    /// Falls back to a random sky-level offset near the player if no crows are active.
    /// </summary>
    private Vector3 GetRandomActiveCrowPosition()
    {
        List<CrowAgent> active = new List<CrowAgent>();
        foreach (var crow in murderCrows)
            if (crow != null && crow.isActiveAndEnabled)
                active.Add(crow);

        if (crow0 != null && crow0.isActiveAndEnabled)
            active.Add(crow0);

        if (active.Count > 0)
            return active[Random.Range(0, active.Count)].transform.position;

        // Fallback: random point in the sky around the player
        float fallbackAngle = Random.Range(0f, Mathf.PI * 2f);
        return player.position + new Vector3(
            Mathf.Cos(fallbackAngle) * 10f,
            12f,
            Mathf.Sin(fallbackAngle) * 10f);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Audio Helpers

    private void PlayFlockCaw()
    {
        // Pick a random active murder crow as the positional source
        List<CrowAgent> active = new List<CrowAgent>();
        foreach (var crow in murderCrows)
            if (crow != null && crow.isActiveAndEnabled)
                active.Add(crow);

        if (active.Count == 0) return;

        CrowAgent src = active[Random.Range(0, active.Count)];
        PlayAt(flockAudioSource, GetRandom(cawClips), src.transform.position);
    }

    /// <summary>
    /// Moves an AudioSource to a world position and plays a one-shot clip there.
    /// Using PlayOneShot allows overlapping calls on the same source without cutting off.
    /// </summary>
    private void PlayAt(AudioSource source, AudioClip clip, Vector3 position)
    {
        if (source == null || clip == null) return;
        source.transform.position = position;
        source.PlayOneShot(clip);
    }

    private AudioClip GetRandom(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Validation

    private void ValidateSetup()
    {
        if (crow0 == null)
            Debug.LogError("[CrowManager] ❌ Crow 0 is not assigned. Drag 'crow 0' into the Inspector.");

        if (murderCrows.Count == 0)
            Debug.LogWarning("[CrowManager] ⚠ No murder crows assigned. Add crow 1–30 to the list.");

        if (murderCrows.Count != 30)
            Debug.LogWarning($"[CrowManager] ⚠ Expected 30 murder crows, got {murderCrows.Count}.");

        if (crow0AudioSource == null)
            Debug.LogWarning("[CrowManager] ⚠ Crow 0 AudioSource not assigned — Crow 0 will be silent.");

        if (flockAudioSource == null)
            Debug.LogWarning("[CrowManager] ⚠ Flock AudioSource not assigned — flock will be silent.");

        if (summonAudioSource == null)
            Debug.LogWarning("[CrowManager] ⚠ Summon AudioSource not assigned — summon screech will be silent.");
    }

    #endregion
}