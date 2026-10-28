using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on anything the weapon can cut (Lotus bud, Bottle Gourd...).
/// Every field is a plain, individually named slot so the Inspector shows them immediately.
///   Bottle Gourd : maxHits 3, triggerCollider1 + 2, animator1 + 2, freezeAfterXRotation OFF, defeatedSound = weapon falling
///   Lotus        : maxHits 5, trapScript1, triggerCollider1, animator1, freezeAfterXRotation ON (180), defeatedSound = lilies emerging
/// When the target is defeated it also plays its sound and turns off its lights (win state).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class KillAimTarget : MonoBehaviour, IWeaponHittable
{
    [Header("Hit Settings")]
    [SerializeField] private int maxHits = 3;
    [Tooltip("Tag the held weapon must have (empty = any weapon)")]
    [SerializeField] private string requiredWeaponTag = "Arriyal weapon";
    [Tooltip("Delay between hits so one swing doesn't count several times")]
    [SerializeField] private float hitCooldown = 0.35f;

    [Header("Trap Scripts To Disable (up to 2)")]
    [SerializeField] private MonoBehaviour trapScript1;
    [SerializeField] private MonoBehaviour trapScript2;

    [Header("Trigger Colliders To Disable (up to 2)")]
    [SerializeField] private Collider triggerCollider1;
    [SerializeField] private Collider triggerCollider2;

    [Header("Animators To Freeze (up to 2)")]
    [SerializeField] private Animator animator1;
    [SerializeField] private Animator animator2;

    [Header("Win State: Lights To Turn Off")]
    [Tooltip("Every light here is switched off when this target is defeated (Area Light, Point Light, any Light)")]
    [SerializeField] private Light[] lightsToTurnOff;
    [Tooltip("0 = switch off instantly. Above 0 = fade the light intensity out over this many seconds, then disable it.")]
    [SerializeField] private float lightFadeSeconds = 0f;

    [Header("Sound Effects")]
    [Tooltip("Optional. Plays the sounds from this object. Leave empty to use a temporary 3D sound at the target's position.")]
    [SerializeField] private AudioSource sfxSource;
    [Tooltip("Optional: played on every registered hit (e.g. weapon slash)")]
    [SerializeField] private AudioClip hitSound;
    [Tooltip("Played when this target is defeated. Bottle Gourd = weapon falling sound. Lotus = lily emerging sound.")]
    [SerializeField] private AudioClip defeatedSound;
    [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;

    [Header("Hit Feedback (every counted hit before the last)")]
    [Tooltip("How far the target shakes on a counted hit (metres). Grows with each hit so the player feels progress.")]
    [SerializeField] private float hitShakeAmount = 0.08f;
    [SerializeField] private float hitShakeSeconds = 0.35f;
    [Tooltip("Tint flashed on the target's renderers on a counted hit")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.85f, 0.6f);
    [SerializeField] private float hitFlashSeconds = 0.25f;
    [Tooltip("Hook extra reactions here (e.g. the gourd's crying girl cries out: SnippetAudioPlayer.PlaySnippetNow)")]
    public UnityEngine.Events.UnityEvent onHit;

    [Header("Locked Feedback (while guarded, e.g. the gourd before its vines are beaten)")]
    [Tooltip("Optional: played when a locked target is hit (a dull 'deflect' sound)")]
    [SerializeField] private AudioClip lockedHitSound;
    [Tooltip("How far the target shakes when hit while locked (metres)")]
    [SerializeField] private float lockedShakeAmount = 0.04f;
    [SerializeField] private float lockedShakeSeconds = 0.25f;

    [Header("Background Music (BGM)")]
    [Tooltip("e.g. 'game bgm question'. Starts when the game starts. If several targets have the same clip it only plays once.")]
    [SerializeField] private AudioClip bgmClip;
    [Range(0f, 1f)] [SerializeField] private float bgmVolume = 0.5f;
    [SerializeField] private bool playBgmOnStart = true;
    [Tooltip("Seconds the BGM takes to fade in when the game starts")]
    [SerializeField] private float bgmFadeInSeconds = 3f;

    [Header("Extra Parts (optional)")]
    [Tooltip("Other colliders (e.g. the stem) that count as hits on this target")]
    [SerializeField] private Collider[] additionalHitColliders;
    [Tooltip("Other objects (e.g. the stem) that fall together with this target; each needs a Rigidbody")]
    [SerializeField] private Transform[] alsoDrop;

    [Header("Fall Physics")]
    [SerializeField] private float impulseForce = 3f;
    [Tooltip("Extra parts (the stem) stop and freeze after falling this far, so they can't drop through the world. 0 = never freeze.")]
    [SerializeField] private float extraPartsMaxFall = 1.5f;
    [Tooltip("ON for the Lotus: lets it tumble, then locks its rotation. OFF for the Bottle Gourd: it just drops.")]
    [SerializeField] private bool freezeAfterXRotation = false;
    [Tooltip("Degrees to rotate around X before the rotation is locked")]
    [SerializeField] private float targetXRotationToFreeze = 180f;
    [Tooltip("Lock rotation anyway after this many seconds if it hasn't reached the target rotation")]
    [SerializeField] private float freezeTimeout = 6f;

    private Rigidbody rb;
    private int currentHits;
    private float lastHitTime = -10f;
    private bool isFallen;
    private Coroutine shakeRoutine;
    private Coroutine flashRoutine;
    private Vector3 shakeRestPosition;

    public bool IsFallen => isFallen;
    public bool AcceptsHits => !isFallen;

    /// <summary>
    /// While locked, hits don't count: the target only shakes and plays the locked sound.
    /// Set by an encounter script (e.g. BottleGourdEncounter keeps the gourd locked until its guard vines are beaten).
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>Raised once, when the target is cut down.</summary>
    public event System.Action<KillAimTarget> Defeated;

    // Lets the cutting system find this target from any of its colliders (parents, own Rigidbody, or the extra colliders above)
    private static readonly Dictionary<Collider, KillAimTarget> registry = new Dictionary<Collider, KillAimTarget>();

    // One shared BGM player for the whole game, so two targets with the same clip don't start it twice
    private static AudioSource sharedBgmSource;

    /// <summary>The game BGM player (null until a target starts it). Cutscenes fade it out and back in.</summary>
    public static AudioSource SharedBgmSource => sharedBgmSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        registry.Clear();
        sharedBgmSource = null;
    }

    public static KillAimTarget FindFor(Collider c)
    {
        if (c == null) return null;
        KillAimTarget t = c.GetComponentInParent<KillAimTarget>();
        if (t == null && c.attachedRigidbody != null) t = c.attachedRigidbody.GetComponent<KillAimTarget>();
        if (t == null) registry.TryGetValue(c, out t);
        return t;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;     // stays fixed until it is cut down
        rb.useGravity = false;

        if (additionalHitColliders != null)
            foreach (var c in additionalHitColliders)
                if (c != null) registry[c] = this;

        if (alsoDrop != null)
            foreach (var t in alsoDrop)
            {
                if (t == null) continue;
                var r = t.GetComponent<Rigidbody>();
                if (r != null) { r.isKinematic = true; r.useGravity = false; }
            }
    }

    private void Start()
    {
        if (playBgmOnStart && bgmClip != null) StartBgm();
    }

    private void OnDestroy()
    {
        if (additionalHitColliders == null) return;
        foreach (var c in additionalHitColliders)
            if (c != null && registry.TryGetValue(c, out var t) && t == this) registry.Remove(c);
    }

    // ----------------------------------------------------------------------------------------------------
    // Audio
    // ----------------------------------------------------------------------------------------------------
    private void StartBgm()
    {
        if (sharedBgmSource != null && sharedBgmSource.isPlaying) return;   // already playing

        if (sharedBgmSource == null)
        {
            var go = new GameObject("BGM (KillAimTarget)");
            sharedBgmSource = go.AddComponent<AudioSource>();
            sharedBgmSource.spatialBlend = 0f;   // 2D: same volume everywhere
            sharedBgmSource.loop = true;
            sharedBgmSource.playOnAwake = false;
        }

        sharedBgmSource.clip = bgmClip;
        sharedBgmSource.volume = bgmFadeInSeconds > 0f ? 0f : bgmVolume;
        sharedBgmSource.Play();
        if (bgmFadeInSeconds > 0f) StartCoroutine(FadeBgmIn(sharedBgmSource, bgmVolume, bgmFadeInSeconds));
        Debug.Log($"[KillAimTarget] BGM '{bgmClip.name}' started.", this);
    }

    private static IEnumerator FadeBgmIn(AudioSource source, float target, float seconds)
    {
        for (float t = 0f; t < seconds && source != null; t += Time.unscaledDeltaTime)
        {
            source.volume = target * (t / seconds);
            yield return null;
        }
        if (source != null) source.volume = target;
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null) return;

        if (sfxSource != null)
        {
            sfxSource.PlayOneShot(clip, sfxVolume);
        }
        else
        {
            // temporary 3D sound at this spot; keeps playing even if this object is moved or disabled
            AudioSource.PlayClipAtPoint(clip, transform.position, sfxVolume);
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Hits
    // ----------------------------------------------------------------------------------------------------
    public void RegisterHit(Vector3 hitPoint, Vector3 swingDirection)
    {
        RegisterHit(requiredWeaponTag, hitPoint, swingDirection);
    }

    public void RegisterHit(string weaponTag, Vector3 hitPoint, Vector3 swingDirection)
    {
        if (isFallen) return;
        if (Time.time - lastHitTime < hitCooldown) return;   // silent: this is called every frame while swinging

        if (!string.IsNullOrEmpty(requiredWeaponTag) && weaponTag != requiredWeaponTag)
        {
            Debug.LogWarning($"[KillAimTarget] '{name}' was hit with a weapon tagged '{weaponTag}' but needs '{requiredWeaponTag}'.", this);
            return;
        }

        lastHitTime = Time.time;

        if (Locked)
        {
            // Guarded: tell the player the hit landed but did nothing yet
            PlaySfx(lockedHitSound);
            Shake(lockedShakeAmount, lockedShakeSeconds);
            return;
        }

        currentHits++;
        PlaySfx(hitSound);

        if (currentHits >= maxHits)
        {
            DropAndFall(swingDirection);
            return;
        }

        // Hits 1..max-1: shake harder each time, flash, and let listeners react (the girl cries out)
        Shake(hitShakeAmount * currentHits, hitShakeSeconds);
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
        onHit?.Invoke();
    }

    private void Shake(float amount, float seconds)
    {
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        else shakeRestPosition = transform.position;   // only capture rest when not already mid-shake
        shakeRoutine = StartCoroutine(ShakeRoutine(amount, seconds));
    }

    // Tints only this target (MaterialPropertyBlock), fading back to the material colour
    private IEnumerator FlashRoutine()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        var block = new MaterialPropertyBlock();
        int baseColorId = Shader.PropertyToID("_BaseColor");
        for (float t = 0f; t < hitFlashSeconds; t += Time.deltaTime)
        {
            float k = t / hitFlashSeconds;
            foreach (var r in renderers)
            {
                var m = r.sharedMaterial;
                if (m == null || !m.HasProperty(baseColorId)) continue;
                r.GetPropertyBlock(block);
                block.SetColor(baseColorId, Color.Lerp(hitFlashColor, m.GetColor(baseColorId), k));
                r.SetPropertyBlock(block);
            }
            yield return null;
        }
        foreach (var r in renderers) r.SetPropertyBlock(null);
        flashRoutine = null;
    }

    private void DropAndFall(Vector3 swingDirection)
    {
        isFallen = true;

        if (trapScript1 != null) trapScript1.enabled = false;
        if (trapScript2 != null) trapScript2.enabled = false;
        if (triggerCollider1 != null) triggerCollider1.enabled = false;
        if (triggerCollider2 != null) triggerCollider2.enabled = false;
        if (animator1 != null) animator1.enabled = false;
        if (animator2 != null) animator2.enabled = false;

        // Win state: sound + lights
        PlaySfx(defeatedSound);
        TurnOffLights();

        MakeDynamic(rb);
        rb.AddForceAtPosition(swingDirection.normalized * impulseForce, transform.position, ForceMode.Impulse);

        if (alsoDrop != null)
            foreach (var t in alsoDrop)
            {
                if (t == null) continue;
                var r = t.GetComponent<Rigidbody>();
                if (r == null) { Debug.LogWarning($"[KillAimTarget] '{t.name}' is in Also Drop but has no Rigidbody.", t); continue; }
                MakeDynamic(r);
                r.AddForce(swingDirection.normalized * impulseForce, ForceMode.Impulse);
                if (extraPartsMaxFall > 0f) StartCoroutine(FreezeAfterFallRoutine(r, extraPartsMaxFall));
            }

        Debug.Log($"[KillAimTarget] {name} cut down and falling.", this);

        if (freezeAfterXRotation) StartCoroutine(FreezeAfterXRotationRoutine());

        Defeated?.Invoke(this);
    }

    // Small random jitter around the rest position; the target is kinematic until it falls, so moving the transform is safe
    private IEnumerator ShakeRoutine(float amount, float seconds)
    {
        for (float t = 0f; t < seconds && !isFallen; t += Time.deltaTime)
        {
            float fade = 1f - t / seconds;
            transform.position = shakeRestPosition + Random.insideUnitSphere * amount * fade;
            yield return null;
        }
        if (!isFallen) transform.position = shakeRestPosition;
        shakeRoutine = null;
    }

    private void TurnOffLights()
    {
        if (lightsToTurnOff == null) return;

        foreach (var l in lightsToTurnOff)
        {
            if (l == null) continue;

            if (lightFadeSeconds > 0f) StartCoroutine(FadeOutLight(l, lightFadeSeconds));
            else l.enabled = false;
        }

        Debug.Log($"[KillAimTarget] {name}: turned off {lightsToTurnOff.Length} light(s).", this);
    }

    private IEnumerator FadeOutLight(Light l, float seconds)
    {
        float start = l.intensity;
        float t = 0f;
        while (t < seconds && l != null)
        {
            t += Time.deltaTime;
            l.intensity = Mathf.Lerp(start, 0f, t / seconds);
            yield return null;
        }

        if (l == null) yield break;
        l.enabled = false;
        l.intensity = start;   // restore so re-enabling the light later isn't dark
    }

    // A non-convex MeshCollider can't be used on a moving Rigidbody, so make mesh colliders convex first
    private static void MakeDynamic(Rigidbody body)
    {
        foreach (var mc in body.GetComponentsInChildren<MeshCollider>(true))
            if (!mc.convex) mc.convex = true;
        body.isKinematic = false;
        body.useGravity = true;
    }

    // Holds a falling part in place once it has dropped far enough (or after a few seconds)
    private IEnumerator FreezeAfterFallRoutine(Rigidbody part, float maxFall)
    {
        float startY = part.position.y;
        float started = Time.time;
        while (part != null && startY - part.position.y < maxFall && Time.time - started < 4f)
            yield return null;
        if (part == null) yield break;

        part.linearVelocity = Vector3.zero;
        part.angularVelocity = Vector3.zero;
        part.isKinematic = true;
        Debug.Log($"[KillAimTarget] '{part.name}' stopped after falling {startY - part.position.y:F2} m.", part);
    }

    private IEnumerator FreezeAfterXRotationRoutine()
    {
        float totalX = 0f;
        float prevX = transform.eulerAngles.x;
        float stopAt = Mathf.Max(0f, targetXRotationToFreeze - 10f);
        float started = Time.time;

        while (totalX < stopAt && Time.time - started < freezeTimeout)
        {
            yield return null;
            float x = transform.eulerAngles.x;
            totalX += Mathf.Abs(Mathf.DeltaAngle(prevX, x));
            prevX = x;

            // It came to rest early (e.g. landed in the water) - lock where it is
            if (rb.linearVelocity.sqrMagnitude < 0.01f && totalX > 45f) break;
        }

        rb.constraints = RigidbodyConstraints.FreezeRotation;
        Debug.Log($"[KillAimTarget] {name} rotation locked after {totalX:F0} degrees.", this);
    }
}
