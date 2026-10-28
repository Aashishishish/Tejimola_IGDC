using System;
using UnityEngine;

/// <summary>
/// Individual crow brain. Handles all movement states:
/// FlyingIn → Circling → Diving → PullingUp, and optional Perching.
/// The CrowManager drives when each state begins — this script only
/// executes the motion for the state it is told to be in.
/// </summary>
public class CrowAgent : MonoBehaviour
{
    public enum State
    {
        Inactive,
        FlyingIn,
        FlyingOut,
        Circling,
        Diving,
        PullingUp,
        Perching
    }

    // ─── Inspector ───────────────────────────────────────────────────────────

    [Header("Current State (Read-Only in Play)")]
    [SerializeField] private State currentState = State.Inactive;
    public State CurrentState => currentState;

    [Header("Aggressive Mode — Crow 0 / Tejimola")]
    [Tooltip("Flies lower, dives closer to the player, and pulls up slower. Enable for Crow 0.")]
    [SerializeField] private bool isAggressiveCrow = false;

    [Header("Orbit / Circling")]
    [Tooltip("The transform to orbit (set automatically by CrowManager).")]
    [SerializeField] private Transform orbitCenter;
    [SerializeField] private float orbitRadius    = 10f;
    [SerializeField] private float orbitHeight    = 14f;
    [SerializeField] private float circleSpeed    = 8f;
    [SerializeField] private float turnSpeed      = 6f;
    [Tooltip("How far ahead on the circle the crow looks (radians). ~0.35–0.5 = natural banking.")]
    [SerializeField] private float lookAheadOffset = 0.4f;

    [Header("Aggressive Overrides (Crow 0 only)")]
    [SerializeField] private float aggressiveOrbitHeight    = 7f;
    [SerializeField] private float aggressivePullUpThreshold = 1.8f;
    [SerializeField] private float aggressivePullUpSpeed    = 10f;

    [Header("Player Follow Lag")]
    [Tooltip("How fast the murder crow's orbit center drifts toward the player. " +
             "Lower = more lag, more natural. ~1.5 is a noticeable delay.")]
    [SerializeField] private float centerFollowSpeed = 1.5f;

    [Tooltip("Follow speed used when isAggressiveCrow is true (Crow 0 tracks faster).")]
    [SerializeField] private float aggressiveCenterFollowSpeed = 5f;

    [Header("Dive-Bomb")]
    [SerializeField] private float diveSpeed          = 22f;
    [SerializeField] private float pullUpSpeed        = 16f;
    [SerializeField] private float targetHeightOffset = 1.5f;  // head-height above player feet
    [SerializeField] private float pullUpThreshold    = 2.5f;
    [SerializeField] private float overshootDistance  = 16f;

    [Header("Fly-In (murder crow spawn)")]
    [SerializeField] private float flyInSpeed           = 18f;
    [SerializeField] private float flyInArrivalDistance = 2.5f;

    [Header("Perching")]
    [SerializeField] private float perchDuration      = 4f;
    [SerializeField] private float perchDescentSpeed  = 9f;
    [SerializeField] private float perchAscendSpeed   = 13f;
    [Tooltip("Layers a crow can land on. Assign 'Default' / 'Ground' / 'Terrain' here.")]
    [SerializeField] private LayerMask perchLayerMask = ~0;

    [Header("Model Alignment")]
    [Tooltip("Rotation offset to correct imported model orientation.")]
    [SerializeField] private Vector3 modelRotationOffset = new Vector3(90f, 0f, 0f);

    // ─── Runtime ─────────────────────────────────────────────────────────────

    // Navigation
    private Transform currentTarget;
    private Vector3   currentCenterPoint;
    private Vector3   diveTargetPoint;
    private Vector3   pullUpTargetPoint;
    private Vector3   diveHorizontalDirection;
    private float     currentAngle;

    // Fly-in / Fly-out
    private Vector3 flyInTargetPosition;
    private Vector3 flyOutTargetPosition;

    // Perching
    private Vector3 perchPoint;
    private float   perchTimer;
    private bool    isDescendingToPerch;

    // ─── Events ──────────────────────────────────────────────────────────────

    /// <summary>Fired when a dive completes and the crow returns to circling.</summary>
    public event Action OnDiveCompleted;

    // ─── Effective values (aggressive overrides for Crow 0) ──────────────────

    private float EffectiveOrbitHeight     => isAggressiveCrow ? aggressiveOrbitHeight     : orbitHeight;
    private float EffectivePullUpThreshold => isAggressiveCrow ? aggressivePullUpThreshold : pullUpThreshold;
    private float EffectivePullUpSpeed     => isAggressiveCrow ? aggressivePullUpSpeed      : pullUpSpeed;

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle

    private void Awake()
    {
        ResolvePlayer();
    }

    private void Update()
    {
        if (currentState == State.Inactive) return;

        if (orbitCenter == null)
        {
            ResolvePlayer();
            if (orbitCenter == null) return;
        }

        // Drift the orbit center toward the player rather than snapping.
        // This gives murder crows a natural lag when the player moves.
        // Crow 0 uses aggressiveCenterFollowSpeed so she tracks tighter.
        float followSpeed = isAggressiveCrow ? aggressiveCenterFollowSpeed : centerFollowSpeed;
        currentCenterPoint = Vector3.Lerp(currentCenterPoint, orbitCenter.position, followSpeed * Time.deltaTime);

        switch (currentState)
        {
            case State.FlyingIn:  HandleFlyingIn();  break;
            case State.FlyingOut: HandleFlyingOut(); break;
            case State.Circling:  HandleCircling();  break;
            case State.Diving:    HandleDiving();    break;
            case State.PullingUp: HandlePullingUp(); break;
            case State.Perching:  HandlePerching();  break;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region State Behaviours

    private void HandleFlyingIn()
    {
        Vector3 dir = (flyInTargetPosition - transform.position).normalized;
        RotateTowards(dir);
        transform.position = Vector3.MoveTowards(
            transform.position, flyInTargetPosition, flyInSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, flyInTargetPosition) <= flyInArrivalDistance)
        {
            // Snap orbit angle to where we arrived
            Vector3 offset = transform.position - currentCenterPoint;
            currentAngle = Mathf.Atan2(offset.z, offset.x);
            currentState = State.Circling;
        }
    }

    private void HandleFlyingOut()
    {
        Vector3 dir = (flyOutTargetPosition - transform.position).normalized;
        RotateTowards(dir);
        transform.position = Vector3.MoveTowards(
            transform.position, flyOutTargetPosition, flyInSpeed * Time.deltaTime);

        // Once far enough away, simply vanish
        if (Vector3.Distance(transform.position, flyOutTargetPosition) <= flyInArrivalDistance)
        {
            currentState = State.Inactive;
            gameObject.SetActive(false);
        }
    }

    private void HandleCircling()
    {
        // Advance angle
        currentAngle += (circleSpeed / orbitRadius) * Time.deltaTime;

        // Target position on the orbit ring
        float targetX = currentCenterPoint.x + Mathf.Cos(currentAngle) * orbitRadius;
        float targetZ = currentCenterPoint.z + Mathf.Sin(currentAngle) * orbitRadius;
        Vector3 targetPosition = new Vector3(targetX, currentCenterPoint.y + EffectiveOrbitHeight, targetZ);

        // Look-ahead target so the crow faces its flight direction, not the center
        float lookAngle = currentAngle + lookAheadOffset;
        float lookX = currentCenterPoint.x + Mathf.Cos(lookAngle) * orbitRadius;
        float lookZ = currentCenterPoint.z + Mathf.Sin(lookAngle) * orbitRadius;
        Vector3 lookTarget = new Vector3(lookX, currentCenterPoint.y + EffectiveOrbitHeight, lookZ);

        RotateTowards((lookTarget - transform.position).normalized);

        // Dynamic speed: catch up if lagging behind
        float dist = Vector3.Distance(transform.position, targetPosition);
        float dynamicSpeed = circleSpeed + (dist * 3f);
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, dynamicSpeed * Time.deltaTime);
    }

    private void HandleDiving()
    {
        // Track the player — the dive target follows them mid-air
        if (currentTarget != null)
        {
            diveTargetPoint = currentTarget.position + Vector3.up * targetHeightOffset;
            Vector3 toTarget = diveTargetPoint - transform.position;
            toTarget.y = 0;
            if (toTarget.sqrMagnitude > 0.05f)
                diveHorizontalDirection = toTarget.normalized;
        }

        RotateTowards((diveTargetPoint - transform.position).normalized);
        transform.position = Vector3.MoveTowards(transform.position, diveTargetPoint, diveSpeed * Time.deltaTime);

        // Pull up once close enough
        if (Vector3.Distance(transform.position, diveTargetPoint) <= EffectivePullUpThreshold)
        {
            pullUpTargetPoint = diveTargetPoint
                              + (diveHorizontalDirection * overshootDistance)
                              + Vector3.up * EffectiveOrbitHeight;
            currentState = State.PullingUp;
        }
    }

    private void HandlePullingUp()
    {
        RotateTowards((pullUpTargetPoint - transform.position).normalized);
        transform.position = Vector3.MoveTowards(
            transform.position, pullUpTargetPoint, EffectivePullUpSpeed * Time.deltaTime);

        float targetAltitude = currentCenterPoint.y + EffectiveOrbitHeight;
        bool reachedAltitude = transform.position.y >= targetAltitude - 1.5f;
        bool reachedPoint    = Vector3.Distance(transform.position, pullUpTargetPoint) < 2.0f;

        if (reachedAltitude || reachedPoint)
        {
            // Re-sync orbit angle to actual position
            Vector3 offset = transform.position - currentCenterPoint;
            currentAngle = Mathf.Atan2(offset.z, offset.x);
            currentState = State.Circling;

            // Notify the manager — used by Crow 0 to count dives
            OnDiveCompleted?.Invoke();
        }
    }

    private void HandlePerching()
    {
        if (isDescendingToPerch)
        {
            // Fly down to the perch point
            RotateTowards((perchPoint - transform.position).normalized);
            transform.position = Vector3.MoveTowards(
                transform.position, perchPoint, perchDescentSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, perchPoint) < 0.25f)
            {
                // Landed — face a random direction and sit
                transform.position = perchPoint;
                transform.rotation = Quaternion.Euler(modelRotationOffset)
                                   * Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                isDescendingToPerch = false;
                perchTimer = perchDuration + UnityEngine.Random.Range(-1f, 1f);
            }
        }
        else
        {
            // Sitting — count down
            perchTimer -= Time.deltaTime;
            if (perchTimer <= 0f)
            {
                // Return to orbit altitude and resume circling
                Vector3 returnTarget = GetOrbitPosition(currentAngle);
                pullUpTargetPoint = returnTarget;
                currentState = State.PullingUp;
            }
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API — called by CrowManager

    /// <summary>Start a dive attack toward the given target.</summary>
    public void StartDive(Transform target)
    {
        if (currentState != State.Circling) return;

        currentTarget = target != null ? target : orbitCenter;

        if (currentTarget != null)
        {
            diveTargetPoint = currentTarget.position + Vector3.up * targetHeightOffset;
            Vector3 toTarget = diveTargetPoint - transform.position;
            toTarget.y = 0;
            diveHorizontalDirection = toTarget.normalized;
        }

        currentState = State.Diving;
    }

    /// <summary>
    /// Sends this crow flying away to a random far point and deactivates it on arrival.
    /// Used when the murder retreats after the player hides too long.
    /// </summary>
    public void FlyAway(Vector3 playerPos, float minDist = 60f, float maxDist = 100f)
    {
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float dist  = UnityEngine.Random.Range(minDist, maxDist);

        // Fly out at roughly the same world height the crow is already at
        flyOutTargetPosition = new Vector3(
            playerPos.x + Mathf.Cos(angle) * dist,
            transform.position.y,
            playerPos.z + Mathf.Sin(angle) * dist);

        currentState = State.FlyingOut;
    }

    /// <summary>
    /// Try to land this crow on a surface below it.
    /// Returns true if a valid surface was found and perching started.
    /// </summary>
    public bool TryPerch()
    {
        if (currentState != State.Circling) return false;

        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 40f, perchLayerMask))
        {
            perchPoint = hit.point + Vector3.up * 0.05f;
            isDescendingToPerch = true;
            currentState = State.Perching;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Spawns this crow at a random far-away position and starts flying toward the orbit ring.
    /// </summary>
    public void SpawnFarAway(Vector3 playerPos,
                             float minDist    = 60f,
                             float maxDist    = 100f,
                             float spawnHeight = 15f)
    {
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float dist  = UnityEngine.Random.Range(minDist, maxDist);

        Vector3 spawnPos = playerPos
                         + new Vector3(Mathf.Cos(angle) * dist, spawnHeight, Mathf.Sin(angle) * dist);
        transform.position = spawnPos;

        // Snap the orbit center immediately to the player so the crow doesn't
        // start lerping from world-origin (Vector3.zero) on its first frame.
        if (orbitCenter != null)
            currentCenterPoint = orbitCenter.position;

        // Pick a random orbit arrival angle so crows spread around the ring
        float orbitAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        currentAngle        = orbitAngle;
        flyInTargetPosition = GetOrbitPosition(orbitAngle);

        currentState = State.FlyingIn;

    }

    /// <summary>Assign where the crow should orbit around.</summary>
    public void SetOrbitCenter(Transform newCenter)
    {
        orbitCenter = newCenter;
        ResolvePlayer();
    }

    /// <summary>Directly set the crow's orbit parameters (used for per-crow variation).</summary>
    public void SetOrbitParameters(float radius, float height, float speed, float lookAhead)
    {
        orbitRadius     = radius;
        orbitHeight     = height;
        circleSpeed     = speed;
        lookAheadOffset = lookAhead;
    }

    /// <summary>Sets the current orbit angle (used to spread crows around the ring on spawn).</summary>
    public void SetAngle(float angleInRadians) => currentAngle = angleInRadians;

    /// <summary>Toggle aggressive mode (lower altitude, slower pull-up — for Crow 0).</summary>
    public void SetAsAggressiveCrow(bool aggressive) => isAggressiveCrow = aggressive;

    /// <summary>Force a state change (used by manager to activate an idle crow).</summary>
    public void SetState(State state) => currentState = state;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Helpers

    private void ResolvePlayer()
    {
        if (orbitCenter == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) orbitCenter = p.transform;
        }

        if (orbitCenter != null)
        {
            // Prefer the CharacterController child (the actual moving root)
            CharacterController cc = orbitCenter.GetComponentInChildren<CharacterController>();
            if (cc != null)
            {
                orbitCenter = cc.transform;
            }
            else
            {
                Transform armature = orbitCenter.Find("PlayerArmature");
                if (armature != null) orbitCenter = armature;
            }
        }

        currentTarget = orbitCenter;
    }

    /// <summary>Returns the world-space orbit position at the given angle.</summary>
    private Vector3 GetOrbitPosition(float angle)
    {
        if (orbitCenter == null) return transform.position;

        float x = orbitCenter.position.x + Mathf.Cos(angle) * orbitRadius;
        float z = orbitCenter.position.z + Mathf.Sin(angle) * orbitRadius;
        return new Vector3(x, orbitCenter.position.y + EffectiveOrbitHeight, z);
    }

    private void RotateTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return;
        Quaternion targetRot = Quaternion.LookRotation(direction) * Quaternion.Euler(modelRotationOffset);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
    }

    #endregion
}