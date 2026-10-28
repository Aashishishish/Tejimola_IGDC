using UnityEngine;

/// <summary>
/// On MainCamera. Holds the weapon (picked up / dropped with E by FinchInteractionSystem + this script)
/// and cuts KillAimTargets while the swing key (X) is held. Move the mouse while holding X to swing the blade.
/// </summary>
public class InspectAndCutSystem : MonoBehaviour
{
    [Header("Anchors & Layers")]
    [SerializeField] private Transform weaponHoldAnchor;
    [Tooltip("Every layer the weapon can hit: killAimLayer (gourd, lotus) and vine (the guard vines)")]
    [SerializeField] private LayerMask killAimLayer;

    [Header("Keys")]
    [Tooltip("Picks the weapon up (handled by FinchInteractionSystem) and drops it again")]
    [SerializeField] private KeyCode pickupDropKey = KeyCode.E;
    [Tooltip("Press to swing the weapon once, from right to left")]
    [SerializeField] private KeyCode swingKey = KeyCode.X;

    [Header("Hold Pose (relative to anchor)")]
    [SerializeField] private Vector3 heldLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 heldLocalEuler = Vector3.zero;

    [Header("Swing Motion & Amplitude")]
    [Tooltip("Total sideways angle of the slash, from the right to the left (degrees)")]
    [SerializeField] private float swingArc = 100f;
    [Tooltip("Degrees the blade is raised at the start and lowered at the end of the slash")]
    [SerializeField] private float swingLift = 15f;
    [Tooltip("How far the blade is tipped forward (away from the camera) at the end of the slash (degrees)")]
    [SerializeField] private float swingForwardTilt = 40f;
    [Tooltip("How far the weapon is pushed out in front of the camera during the slash (metres)")]
    [SerializeField] private float swingReach = 0.5f;
    [SerializeField] private float windUpTime = 0.12f;
    [Tooltip("Seconds the slash takes")]
    [SerializeField] private float swingDuration = 0.22f;
    [SerializeField] private float swingRecoverTime = 0.3f;
    [SerializeField] private float wristRollFactor = 0.4f;

    [Header("Cutting Detection")]
    [SerializeField] private float cutCheckDistance = 1.8f;
    [Tooltip("Radius of the hit sphere around the blade (world units)")]
    [SerializeField] private float bladeHitRadius = 0.35f;
    [Tooltip("Only count hits while the swing key is held")]
    [SerializeField] private bool requireSwingToCut = true;
    [Tooltip("Print what the cut check sees to the Console (throttled)")]
    [SerializeField] private bool debugCut = false;

    [Header("Weapon Sounds")]
    [Tooltip("Optional. If empty, a 2D AudioSource is added to this object automatically.")]
    [SerializeField] private AudioSource weaponAudio;
    [Tooltip("Played when you start swinging (press X) and on each fast mouse stroke while swinging")]
    [SerializeField] private AudioClip slashSound;
    [Tooltip("Played when you drop the weapon")]
    [SerializeField] private AudioClip dropSound;
    [Range(0f, 1f)] [SerializeField] private float weaponSfxVolume = 0.5f;
    [Tooltip("Minimum seconds between two slash sounds")]
    [SerializeField] private float slashCooldown = 0.5f;
    [Tooltip("Mouse movement per frame that counts as a swing stroke (Input 'Mouse X/Y' units)")]
    [SerializeField] private float slashMouseSpeed = 0.4f;

    private Transform currentHeldObject;
    private Rigidbody heldRigidbody;
    private bool isHolding = false;
    private int equipFrame = -1;
    private float lastDebugTime;
    private float lastSlashTime = -10f;

    private bool swinging;
    private float swingTimer;
    private Vector2 currentSwingOffset;
    private Quaternion defaultAnchorRotation;
    private Vector3 defaultAnchorPosition;

    private void Start()
    {
        if (weaponHoldAnchor != null)
        {
            defaultAnchorRotation = weaponHoldAnchor.localRotation;
            defaultAnchorPosition = weaponHoldAnchor.localPosition;
        }

        if (weaponAudio == null)
        {
            weaponAudio = gameObject.AddComponent<AudioSource>();
            weaponAudio.playOnAwake = false;
            weaponAudio.spatialBlend = 0f;   // 2D: it's your own weapon
        }
    }

    private void PlayWeaponSfx(AudioClip clip)
    {
        if (clip != null && weaponAudio != null) weaponAudio.PlayOneShot(clip, weaponSfxVolume);
    }

    private void Update()
    {
        if (!isHolding) return;

        HandleControlledSwing();
        PerformSliceCheck();

        // Same key that picked it up drops it (ignore the very press that picked it up)
        if (Time.frameCount != equipFrame && Input.GetKeyDown(pickupDropKey))
            DropObject();
    }

    public KeyCode PickupDropKey => pickupDropKey;

    public void EquipObject(Transform targetObject)
    {
        currentHeldObject = targetObject;

        if (GameTasks.Instance != null) GameTasks.Instance.NotifyPickup(targetObject.tag);

        heldRigidbody = currentHeldObject.GetComponent<Rigidbody>();
        if (heldRigidbody != null)
            heldRigidbody.isKinematic = true;

        SetCollidersEnabled(currentHeldObject, false);

        currentHeldObject.SetParent(weaponHoldAnchor);
        currentHeldObject.localPosition = heldLocalPosition;
        currentHeldObject.localRotation = Quaternion.Euler(heldLocalEuler);
        var poseOffset = currentHeldObject.GetComponent<HeldPoseOffset>();
        if (poseOffset != null && !poseOffset.keepUpright) currentHeldObject.localRotation *= Quaternion.Euler(poseOffset.extraEuler);

        currentSwingOffset = Vector2.zero;
        swinging = false;
        isHolding = true;
        equipFrame = Time.frameCount;
    }

    // Press X: one slash from the right side of the screen to the left through a set arc, then back to the hold pose.
    private void HandleControlledSwing()
    {
        if (Input.GetKeyDown(swingKey)) StartSwing();

        if (swinging)
        {
            swingTimer += Time.deltaTime;
            float total = swingDuration + swingRecoverTime;
            float yaw, pitch, roll, reach = 0f;
            if (swingTimer < windUpTime)
            {
                // short wind-up to the right
                float k = Mathf.SmoothStep(0f, 1f, swingTimer / windUpTime);
                yaw = Mathf.Lerp(0f, swingArc * 0.5f, k);
                pitch = Mathf.Lerp(0f, -swingLift, k);
            }
            else if (swingTimer < windUpTime + swingDuration)
            {
                // the slash: right -> left
                float k = (swingTimer - windUpTime) / swingDuration;
                float e = 1f - (1f - k) * (1f - k);
                yaw = Mathf.Lerp(swingArc * 0.5f, -swingArc * 0.5f, e);
                pitch = Mathf.Lerp(-swingLift, swingForwardTilt, e);
                reach = swingReach * Mathf.Sin(Mathf.Clamp01(e) * Mathf.PI * 0.5f);
            }
            else
            {
                float k = Mathf.Clamp01((swingTimer - windUpTime - swingDuration) / swingRecoverTime);
                k = Mathf.SmoothStep(0f, 1f, k);
                yaw = Mathf.Lerp(-swingArc * 0.5f, 0f, k);
                pitch = Mathf.Lerp(swingForwardTilt, 0f, k);
                reach = Mathf.Lerp(swingReach, 0f, k);
                if (k >= 1f) swinging = false;
            }
            roll = -yaw * wristRollFactor;
            currentSwingOffset = new Vector2(pitch, yaw);
            weaponHoldAnchor.localRotation = defaultAnchorRotation * Quaternion.Euler(pitch, yaw, roll);
            weaponHoldAnchor.localPosition = defaultAnchorPosition + Vector3.forward * reach;
        }
        else
        {
            currentSwingOffset = Vector2.zero;
            weaponHoldAnchor.localRotation = defaultAnchorRotation;
            weaponHoldAnchor.localPosition = defaultAnchorPosition;
        }
    }

    public void StartSwing()
    {
        if (swinging || !isHolding) return;
        swinging = true;
        swingTimer = 0f;
        lastSlashTime = Time.time;
        PlayWeaponSfx(slashSound);
    }

    // Only the actual slash (not the wind-up / recovery) can hit something
    private bool InSlashPhase => swinging && swingTimer >= windUpTime * 0.5f && swingTimer <= windUpTime + swingDuration + 0.05f;

    private void PerformSliceCheck()
    {
        if (currentHeldObject == null) return;

        // Aim lines for the Scene view (and the Game view with Gizmos on)
        Vector3 bladeCenter = GetBladeCenter();
        if (debugCut)
        {
            Debug.DrawRay(transform.position, transform.forward * (cutCheckDistance + 0.6f), Color.cyan);          // camera aim
            Debug.DrawRay(currentHeldObject.position, currentHeldObject.forward * cutCheckDistance, Color.red);    // weapon forward
            Debug.DrawLine(bladeCenter, bladeCenter + Vector3.up * bladeHitRadius, Color.yellow);                   // blade hit sphere
        }

        if (requireSwingToCut && !InSlashPhase) return;

        // 1) Anything on the kill layer touching the blade
        Collider[] touching = Physics.OverlapSphere(bladeCenter, bladeHitRadius, killAimLayer, QueryTriggerInteraction.Collide);
        foreach (var c in touching)
            if (TryRegister(c, bladeCenter, transform.forward, "blade sphere")) return;

        // 2) What the crosshair points at (thick ray so thin parts are easier to hit).
        // SphereCastAll + nearest-first, because big trigger zones (e.g. a vine's attack area) share the layer
        // and would otherwise swallow the aim before it reaches the vine or gourd behind them.
        RaycastHit[] hits = Physics.SphereCastAll(transform.position, 0.2f, transform.forward, cutCheckDistance + 0.6f, killAimLayer, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
            if (TryRegister(hit.collider, hit.point, transform.forward, "camera aim")) return;

        if (hits.Length == 0 && debugCut && Time.time - lastDebugTime > 1f)
        {
            lastDebugTime = Time.time;
            Debug.Log($"[Cut] Swinging, but nothing on layer '{LayerMask.LayerToName(FirstLayer(killAimLayer))}' within {bladeHitRadius}m of the blade or {cutCheckDistance + 0.6f}m along the camera aim.");
        }
    }

    private Vector3 GetBladeCenter()
    {
        var rend = currentHeldObject.GetComponentInChildren<Renderer>();
        return rend != null ? rend.bounds.center : currentHeldObject.position;
    }

    private static int FirstLayer(LayerMask mask)
    {
        for (int i = 0; i < 32; i++) if ((mask.value & (1 << i)) != 0) return i;
        return 0;
    }

    private bool TryRegister(Collider c, Vector3 point, Vector3 dir, string source)
    {
        IWeaponHittable target = WeaponHittables.FindFor(c);

        if (target == null)
        {
            if (debugCut && Time.time - lastDebugTime > 1f)
            {
                lastDebugTime = Time.time;
                Debug.LogWarning($"[Cut] {source} hit '{c.name}' (layer {LayerMask.LayerToName(c.gameObject.layer)}) but nothing hittable (KillAimTarget, VineHealth...) is on it, its parents, or in an Additional Hit Colliders list.", c);
            }
            return false;
        }

        if (!target.AcceptsHits) return false;

        if (debugCut) Debug.Log($"[Cut] {source} -> '{c.name}' (hittable '{WeaponHittables.NameOf(target)}'), weapon tag '{currentHeldObject.tag}'", c);
        target.RegisterHit(currentHeldObject.tag, point, dir);
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!isHolding || currentHeldObject == null) return;
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.35f);
        Gizmos.DrawWireSphere(GetBladeCenter(), bladeHitRadius);
    }

    public void DropObject()
    {
        if (currentHeldObject == null) return;

        currentHeldObject.SetParent(null);
        SetCollidersEnabled(currentHeldObject, true);
        var finch = GetComponent<FinchInteractionSystem>();
        if (finch != null) finch.MarkDropped(currentHeldObject);
        PlayWeaponSfx(dropSound);

        if (heldRigidbody != null)
        {
            heldRigidbody.isKinematic = false;
            heldRigidbody.linearVelocity = Vector3.zero;
        }

        weaponHoldAnchor.localRotation = defaultAnchorRotation;
        weaponHoldAnchor.localPosition = defaultAnchorPosition;
        swinging = false;
        currentHeldObject = null;
        isHolding = false;
    }

    private void SetCollidersEnabled(Transform t, bool state)
    {
        foreach (var c in t.GetComponentsInChildren<Collider>(true))
            c.enabled = state;
    }

    public bool IsHoldingObject() => isHolding;
}
