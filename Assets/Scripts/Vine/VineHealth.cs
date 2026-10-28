using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on a guard vine's root (the object with the Animator, e.g. "bottle gourd slam vine").
/// The held weapon can hit it (through IWeaponHittable). Each hit plays a pain reaction:
///   recoil (the whole vine bends away from the swing), writhe (the joints shudder, stronger toward the tip),
///   a red flash, a pain sound, and a short stun (animation slows down and the vine can't hurt the player).
/// After maxHits the vine is beaten: it stops attacking, becomes harmless, thrashes, then falls to the ground
/// with real physics through VineCollapse (or sinks, if Defeat Style is SinkIntoGround).
///
/// No pain animation exists for the vine rig, so everything is procedural and layered on top of the Animator
/// in LateUpdate. If a real "hurt" clip is made later, trigger it from onHit and lower writheAngle.
/// </summary>
[DisallowMultipleComponent]
public class VineHealth : MonoBehaviour, IWeaponHittable
{
    [Header("Health")]
    [SerializeField] private int maxHits = 3;
    [Tooltip("Tag the held weapon must have (empty = any weapon)")]
    [SerializeField] private string requiredWeaponTag = "Arriyal weapon";
    [Tooltip("Delay between hits so one swing doesn't count several times")]
    [SerializeField] private float hitCooldown = 0.35f;

    [Header("Links (auto-found if empty)")]
    [SerializeField] private Animator vineAnimator;
    [Tooltip("The trigger zone that makes this vine attack. It is switched off when the vine is beaten.")]
    [SerializeField] private StartOnTrigger attackTrigger;

    [Header("Pain: Stun")]
    [Tooltip("Seconds the vine is stunned after each hit")]
    [SerializeField] private float stunSeconds = 1.2f;
    [Tooltip("Animation speed while stunned (0 = frozen, 1 = normal)")]
    [Range(0f, 1f)] [SerializeField] private float stunAnimatorSpeed = 0.15f;
    [Tooltip("The vine can't damage the player while stunned: the reward window for landing a hit")]
    [SerializeField] private bool harmlessWhileStunned = true;

    [Header("Pain: Recoil (whole vine bends away from the swing)")]
    [SerializeField] private float recoilAngle = 32f;
    [SerializeField] private float recoilSeconds = 1f;
    [Tooltip("0..1 over time -> fraction of Recoil Angle. Overshooting below 0 makes it spring back past rest.")]
    [SerializeField] private AnimationCurve recoilCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.18f, 1f), new Keyframe(0.32f, 0.85f), new Keyframe(0.6f, -0.3f), new Keyframe(1f, 0f));

    [Header("Pain: Writhe (joints shudder)")]
    [Tooltip("Max extra bend per joint, in degrees (the tip gets the full amount, the base almost none)")]
    [SerializeField] private float writheAngle = 30f;
    [SerializeField] private float writheFrequency = 9f;
    [SerializeField] private float writheSeconds = 1.3f;

    [Header("Pain: Flash")]
    [SerializeField] private Color painColor = new Color(1f, 0.2f, 0.15f);
    [SerializeField] private float flashSeconds = 0.35f;

    [Header("Pain: Blood VFX")]
    [Tooltip("Spawned where the weapon hit (e.g. BloodFlakes_Hit)")]
    [SerializeField] private GameObject hitVfx;
    [SerializeField] private float hitVfxScale = 1f;
    [Tooltip("Extra-big burst on the killing hit")]
    [SerializeField] private float defeatVfxScale = 1.8f;

    [Header("Sound")]
    [Tooltip("One is picked at random on every hit")]
    [SerializeField] private AudioClip[] painSounds;
    [SerializeField] private Vector2 painPitchRange = new Vector2(1.1f, 1.4f);
    [Range(0f, 1f)] [SerializeField] private float painVolume = 0.8f;
    [SerializeField] private AudioClip defeatSound;
    [SerializeField] private float defeatPitch = 0.75f;
    [Range(0f, 1f)] [SerializeField] private float defeatVolume = 1f;
    [SerializeField] private float soundMinDistance = 4f;
    [SerializeField] private float soundMaxDistance = 40f;

    public enum DefeatStyle { FallWithPhysics, SinkIntoGround }

    [Header("Defeat: Thrash, then fall to the ground (or sink)")]
    [SerializeField] private DefeatStyle defeatStyle = DefeatStyle.FallWithPhysics;
    [SerializeField] private float thrashSeconds = 0.6f;
    [Tooltip("FallWithPhysics: does the falling (Rigidbody + gravity). Auto-found on this object.")]
    [SerializeField] private VineCollapse collapse;
    [Tooltip("SinkIntoGround: seconds to sink")]
    [SerializeField] private float sinkSeconds = 1.8f;
    [Tooltip("SinkIntoGround: how far the vine sinks (world metres)")]
    [SerializeField] private float sinkDepth = 7f;
    [Tooltip("SinkIntoGround: degrees the vine leans away from the player while sinking")]
    [SerializeField] private float sinkLean = 40f;
    [Tooltip("Hide the vine when the defeat motion ends (it is already harmless from the moment it is beaten)")]
    [SerializeField] private bool hideWhenDone = false;

    [Header("Events")]
    public UnityEvent onHit;
    public UnityEvent onDefeated;

    /// <summary>Raised once, the moment the vine is beaten (before it finishes sinking).</summary>
    public event System.Action<VineHealth> Defeated;

    public bool IsDefeated => isDefeated;
    public bool AcceptsHits => !isDefeated;
    public int HitsTaken => currentHits;
    public int MaxHits => maxHits;

    private int currentHits;
    private float lastHitTime = -10f;
    private bool isDefeated;

    private VineDamageHitbox[] hitboxes;
    private Transform[] bones;
    private Renderer[] renderers;
    private AudioSource sfx;
    private MaterialPropertyBlock mpb;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    // Root pose is driven from these each LateUpdate (the clips only animate the joints, never this root)
    private Vector3 restPosition;
    private Quaternion restRotation;
    private Vector3 bendAxis = Vector3.right;   // world axis the vine bends around (away from the player)
    private Vector3 awayFromPlayer = Vector3.forward;   // flat swing direction of the last hit
    private bool physicsOwnsRoot;               // after the collapse the Rigidbody moves the root, not us
    private float recoilTimer = -1f;
    private float writheTimer = -1f;
    private float writheStrength = 1f;
    private float sinkAmount;    // 0..1 of sinkDepth
    private float leanDegrees;   // extra bend from the defeat motion

    // The last offset we added to each bone, so it can be removed if the Animator didn't overwrite the bone this frame
    private Quaternion[] lastWritten;
    private Quaternion[] lastOffset;
    private bool bonesHaveOffset;
    private bool rootAtRest = true;

    private float normalAnimatorSpeed = 1f;
    private Coroutine stunRoutine;
    private Coroutine flashRoutine;

    private void Awake()
    {
        if (vineAnimator == null) vineAnimator = GetComponent<Animator>();
        if (vineAnimator != null) normalAnimatorSpeed = vineAnimator.speed;

        hitboxes = GetComponentsInChildren<VineDamageHitbox>(true);
        renderers = GetComponentsInChildren<Renderer>(true);
        bones = CollectBones();
        lastWritten = new Quaternion[bones.Length];
        lastOffset = new Quaternion[bones.Length];
        for (int i = 0; i < bones.Length; i++) lastOffset[i] = Quaternion.identity;

        mpb = new MaterialPropertyBlock();

        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 1f;   // 3D: the scream comes from the vine
        sfx.rolloffMode = AudioRolloffMode.Logarithmic;
        sfx.minDistance = soundMinDistance;
        sfx.maxDistance = soundMaxDistance;

        restPosition = transform.position;
        restRotation = transform.rotation;
    }

    // Skinned mesh bones sorted base -> tip (by hierarchy depth), so the writhe can grow toward the tip
    private Transform[] CollectBones()
    {
        var list = new List<Transform>();
        var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null) foreach (var b in smr.bones) if (b != null) list.Add(b);
        if (list.Count == 0)
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().StartsWith("joint")) list.Add(t);

        list.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
        return list.ToArray();
    }

    private int Depth(Transform t)
    {
        int d = 0;
        while (t != null && t != transform) { d++; t = t.parent; }
        return d;
    }

    // ----------------------------------------------------------------------------------------------------
    // Hits
    // ----------------------------------------------------------------------------------------------------
    public void RegisterHit(string weaponTag, Vector3 hitPoint, Vector3 swingDirection)
    {
        if (isDefeated) return;
        if (Time.time - lastHitTime < hitCooldown) return;   // silent: called every frame while the blade overlaps

        if (!string.IsNullOrEmpty(requiredWeaponTag) && weaponTag != requiredWeaponTag)
        {
            Debug.LogWarning($"[VineHealth] '{name}' was hit with a weapon tagged '{weaponTag}' but needs '{requiredWeaponTag}'.", this);
            return;
        }

        lastHitTime = Time.time;
        currentHits++;
        Debug.Log($"[VineHealth] {name} hit {currentHits}/{maxHits}", this);

        SetBendAxis(swingDirection);
        recoilTimer = 0f;
        writheTimer = 0f;
        writheStrength = 1f;
        Flash();
        PlayPainSound();
        VfxSpawner.Spawn(hitVfx, hitPoint, currentHits >= maxHits ? defeatVfxScale : hitVfxScale);
        onHit?.Invoke();

        if (currentHits >= maxHits) Defeat();
        else Stun();
    }

    // Bending around Cross(up, dir) tilts the vine's up axis toward dir, i.e. away from the player's swing
    private void SetBendAxis(Vector3 swingDirection)
    {
        Vector3 flat = new Vector3(swingDirection.x, 0f, swingDirection.z);
        if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
        awayFromPlayer = flat.normalized;
        bendAxis = Vector3.Cross(Vector3.up, awayFromPlayer).normalized;
    }

    private void Stun()
    {
        if (stunRoutine != null) StopCoroutine(stunRoutine);
        stunRoutine = StartCoroutine(StunRoutine());
    }

    private IEnumerator StunRoutine()
    {
        if (vineAnimator != null) vineAnimator.speed = stunAnimatorSpeed;
        if (harmlessWhileStunned) SetHitboxesEnabled(false);

        yield return new WaitForSeconds(stunSeconds);

        if (isDefeated) yield break;   // Defeat() owns the vine from here
        if (vineAnimator != null) vineAnimator.speed = normalAnimatorSpeed;
        SetHitboxesEnabled(true);
        stunRoutine = null;
    }

    private void SetHitboxesEnabled(bool on)
    {
        foreach (var h in hitboxes) if (h != null) h.enabled = on;
    }

    // ----------------------------------------------------------------------------------------------------
    // Defeat
    // ----------------------------------------------------------------------------------------------------
    private void Defeat()
    {
        isDefeated = true;
        if (stunRoutine != null) StopCoroutine(stunRoutine);

        // Harmless for good: no damage, no attack zone, nothing for the player or weapon to bump into
        SetHitboxesEnabled(false);
        if (attackTrigger != null)
        {
            attackTrigger.ForceStop();
            foreach (var c in attackTrigger.GetComponents<Collider>()) c.enabled = false;
        }
        // Physics fall: VineCollapse turns the colliders solid (ground only, not the player). Otherwise switch them off.
        if (collapse == null) collapse = GetComponent<VineCollapse>();
        if (defeatStyle != DefeatStyle.FallWithPhysics || collapse == null)
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

        PlaySound(defeatSound, defeatPitch, defeatVolume);
        Debug.Log($"[VineHealth] {name} beaten.", this);

        onDefeated?.Invoke();
        Defeated?.Invoke(this);

        StartCoroutine(DefeatRoutine());
    }

    private IEnumerator DefeatRoutine()
    {
        // 1) Death throes: freeze the clip and thrash hard
        if (vineAnimator != null) vineAnimator.speed = 0f;
        for (float t = 0f; t < thrashSeconds; t += Time.deltaTime)
        {
            writheTimer = 0f;   // keep the writhe going at full strength
            writheStrength = 1.8f;
            yield return null;
        }

        if (defeatStyle == DefeatStyle.FallWithPhysics && collapse != null)
        {
            // 2a) Let gravity take it: stop the writhe, hand the root to the Rigidbody, push it away from the player
            writheStrength = 0f;
            writheTimer = -1f;
            recoilTimer = -1f;
            physicsOwnsRoot = true;
            collapse.Collapse(awayFromPlayer);
        }
        else
        {
            // 2b) Sink into the ground while leaning away
            for (float t = 0f; t < sinkSeconds; t += Time.deltaTime)
            {
                float k = t / sinkSeconds;
                sinkAmount = k * k;                              // ease in: slow start, fast drop
                leanDegrees = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 2f)) * sinkLean;
                writheTimer = 0f;
                writheStrength = Mathf.Lerp(1.2f, 0f, k);
                yield return null;
            }
            sinkAmount = 1f;
        }
        writheStrength = 0f;

        // Hide rather than SetActive(false): deactivating would cut off the defeat scream still playing on this object
        if (hideWhenDone)
        {
            foreach (var r in renderers) if (r != null) r.enabled = false;
            if (vineAnimator != null) vineAnimator.enabled = false;
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Procedural pose (after the Animator has written this frame's pose)
    // ----------------------------------------------------------------------------------------------------
    private void LateUpdate()
    {
        ApplyRootPose();
        ApplyWrithe();
    }

    private void ApplyRootPose()
    {
        if (physicsOwnsRoot) return;

        float recoil = 0f;
        if (recoilTimer >= 0f)
        {
            recoilTimer += Time.deltaTime;
            float k = recoilTimer / recoilSeconds;
            if (k >= 1f) recoilTimer = -1f;
            else recoil = recoilCurve.Evaluate(k) * recoilAngle;
        }

        float angle = recoil + leanDegrees;

        // Moving the root also moves every collider on its kinematic Rigidbody, so skip it while the vine is at rest
        bool atRest = angle == 0f && sinkAmount == 0f;
        if (atRest && rootAtRest) return;
        rootAtRest = atRest;

        transform.SetPositionAndRotation(
            restPosition + Vector3.down * (sinkAmount * sinkDepth),
            Quaternion.AngleAxis(angle, bendAxis) * restRotation);
    }

    private void ApplyWrithe()
    {
        float envelope = 0f;
        if (writheTimer >= 0f)
        {
            writheTimer += Time.deltaTime;
            float k = writheTimer / writheSeconds;
            if (k >= 1f) writheTimer = -1f;
            else envelope = (1f - k) * (1f - k) * writheStrength;   // strong at the hit, dies away
        }

        // Nothing to add and nothing left over from last frame: leave the bones to the Animator
        if (envelope <= 0f && !bonesHaveOffset) return;
        bonesHaveOffset = envelope > 0f;

        for (int i = 0; i < bones.Length; i++)
        {
            Transform b = bones[i];
            if (b == null) continue;

            // If the Animator skipped this bone (frozen, culled, disabled) it still holds our last offset: remove it first
            Quaternion current = b.localRotation;
            if (current == lastWritten[i]) current *= Quaternion.Inverse(lastOffset[i]);

            Quaternion offset = Quaternion.identity;
            if (envelope > 0f)
            {
                float weight = (i + 1f) / bones.Length;   // base barely moves, tip whips
                float a = writheAngle * weight * envelope * Mathf.Sin(Time.time * writheFrequency + i * 0.9f);
                Vector3 localAxis = b.parent != null ? b.parent.InverseTransformDirection(bendAxis) : bendAxis;
                // rotate in the parent's space so every joint bends around the same world axis
                offset = Quaternion.Inverse(current) * Quaternion.AngleAxis(a, localAxis) * current;
            }

            b.localRotation = current * offset;
            lastWritten[i] = b.localRotation;
            lastOffset[i] = offset;
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Flash & sound
    // ----------------------------------------------------------------------------------------------------
    private void Flash()
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    // MaterialPropertyBlock tints only this vine (the "tentacles" material is shared by every vine)
    private IEnumerator FlashRoutine()
    {
        for (float t = 0f; t < flashSeconds; t += Time.deltaTime)
        {
            float k = t / flashSeconds;
            foreach (var r in renderers)
            {
                if (r == null || r.sharedMaterial == null) continue;
                var m = r.sharedMaterial;
                int id = m.HasProperty(BaseColorId) ? BaseColorId : (m.HasProperty(ColorId) ? ColorId : -1);
                if (id == -1) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(id, Color.Lerp(painColor, m.GetColor(id), k));
                r.SetPropertyBlock(mpb);
            }
            yield return null;
        }

        // Clear the block so the renderer goes back to the material (and SRP batching)
        foreach (var r in renderers) if (r != null) r.SetPropertyBlock(null);
        flashRoutine = null;
    }

    private void PlayPainSound()
    {
        if (painSounds == null || painSounds.Length == 0) return;
        var clip = painSounds[Random.Range(0, painSounds.Length)];
        PlaySound(clip, Random.Range(painPitchRange.x, painPitchRange.y), painVolume);
    }

    private void PlaySound(AudioClip clip, float pitch, float volume)
    {
        if (clip == null || sfx == null) return;
        sfx.pitch = pitch;
        sfx.PlayOneShot(clip, volume);
    }
}
