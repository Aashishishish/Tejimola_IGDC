using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Dissolves a group of objects into particles: every renderer under Roots sheds blood flakes from its
/// bounds while it shrivels away, then is hidden. Used for the Bottle Gourd reincarnated form when the
/// gourd drops. Call Play() (BottleGourdEncounter does), or hook it to any UnityEvent.
///
/// Why shrink instead of fading: the vine materials are opaque URP Lit; fading would need transparent
/// material swaps. Shrinking toward each piece's base plus a thick flake cloud reads as "evaporating".
/// A dissolve shader can replace the shrink later without changing callers.
/// </summary>
public class EvaporateEffect : MonoBehaviour
{
    [Header("What evaporates")]
    [Tooltip("Everything with a renderer under these evaporates")]
    [SerializeField] private Transform[] roots;
    [Tooltip("Anything under these stays (e.g. the Old Wood Shed)")]
    [SerializeField] private Transform[] keep;
    [Tooltip("On kept objects, the parts using these materials evaporate anyway (e.g. the 'vines' painted on the shed). " +
             "Works when it is the LAST material slot: the renderer simply stops drawing that sub-mesh.")]
    [SerializeField] private Material[] stripFromKept;

    [Header("Particles")]
    [Tooltip("Looping flake system; its Box shape is resized to each renderer (BloodFlakes_Evaporate)")]
    [SerializeField] private ParticleSystem evaporatePrefab;
    [Tooltip("Flakes per second per square metre of the renderer's footprint")]
    [SerializeField] private float flakesPerSquareMetre = 8f;
    [SerializeField] private float maxFlakesPerSecondPerObject = 160f;

    [Header("Timing")]
    [Tooltip("Seconds the flakes pour off")]
    [SerializeField] private float duration = 3f;
    [Tooltip("0..1 of Duration: when the shrinking starts and when the piece is gone")]
    [SerializeField] private Vector2 shrinkWindow = new Vector2(0.25f, 0.85f);
    [Tooltip("0..1 of Duration: each piece starts its shrink up to this much later than the others, " +
             "so the form wastes away bit by bit instead of all at once")]
    [Range(0f, 0.5f)] [SerializeField] private float stagger = 0f;

    [Header("Slow pieces (e.g. the NPC vines)")]
    [Tooltip("Pieces under these use Slow Shrink Window instead: they start early and linger the longest")]
    [SerializeField] private Transform[] slowRoots;
    [SerializeField] private Vector2 slowShrinkWindow = new Vector2(0.05f, 1f);

    [Header("Sound")]
    [Tooltip("Played together the moment the evaporation starts (e.g. a scream + crying)")]
    [SerializeField] private AudioClip[] startSounds;
    [Range(0f, 1f)] [SerializeField] private float startSoundsVolume = 1f;
    [Tooltip("0 = heard the same everywhere (2D), 1 = positioned at this object (3D)")]
    [Range(0f, 1f)] [SerializeField] private float startSoundsSpatialBlend = 0f;
    [Tooltip("Seconds after the start before the sounds fade out (long clips like crying would otherwise run into the cutscene). 0 = play to the end")]
    [SerializeField] private float startSoundsStopAfter = 3f;
    [SerializeField] private float startSoundsFadeSeconds = 1.5f;

    public UnityEvent onEvaporated;

    public float Duration => duration;
    public bool IsPlaying { get; private set; }

    private class Piece
    {
        public Renderer renderer;
        public Transform scaled;     // what we shrink: the root bone for skinned meshes, else the renderer itself
        public Vector3 startScale;
        public ParticleSystem vfx;
        public int stripToMaterialCount = -1;   // kept renderer: cut the material list to this length when gone
        public Vector2 window;                   // this piece's own 0..1 shrink window (stagger / slow roots applied)
    }

    public void Play()
    {
        if (IsPlaying) return;
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        IsPlaying = true;
        PlayStartSounds();
        var pieces = CollectPieces();

        foreach (var p in pieces)
        {
            p.window = PieceWindow(p.renderer.transform);
            Bounds b = p.renderer.bounds;
            if (evaporatePrefab != null)
            {
                p.vfx = Instantiate(evaporatePrefab, b.center, Quaternion.identity);
                foreach (var ps in p.vfx.GetComponentsInChildren<ParticleSystem>())
                {
                    var shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = b.size;
                    shape.rotation = Vector3.zero;
                    var emission = ps.emission;
                    // the root (flakes) gets the full rate; the chunk child keeps its own small share
                    float area = Mathf.Max(0.25f, b.size.x * b.size.z + b.size.y * Mathf.Max(b.size.x, b.size.z));
                    float scaleFactor = Mathf.Min(area * flakesPerSquareMetre, maxFlakesPerSecondPerObject) / 60f;
                    emission.rateOverTimeMultiplier *= scaleFactor;
                }
            }
        }

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            foreach (var p in pieces)
            {
                float s = 1f - Mathf.Clamp01(Mathf.InverseLerp(p.window.x, p.window.y, k));
                s *= s;   // ease-in: holds its shape, then goes quickly
                if (p.stripToMaterialCount >= 0)
                {
                    if (s <= 0f) Strip(p);
                    continue;
                }
                if (p.scaled == null) continue;
                p.scaled.localScale = p.startScale * Mathf.Max(s, 0.0001f);
                if (s <= 0f && p.renderer != null) p.renderer.enabled = false;
            }
            yield return null;
        }

        foreach (var p in pieces)
        {
            if (p.stripToMaterialCount >= 0) Strip(p);
            else if (p.renderer != null) p.renderer.enabled = false;
            if (p.vfx == null) continue;
            p.vfx.Stop(true, ParticleSystemStopBehavior.StopEmitting);   // flakes already in the air finish their flight
            var main = p.vfx.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
        }

        IsPlaying = false;
        onEvaporated?.Invoke();
    }

    private List<Piece> CollectPieces()
    {
        var pieces = new List<Piece>();
        var scaledAlready = new HashSet<Transform>();
        if (roots == null) return pieces;

        foreach (var root in roots)
        {
            if (root == null) continue;

            // Freeze everything first: animators would overwrite the shrinking bones, physics would fight the scaling
            foreach (var a in root.GetComponentsInChildren<Animator>()) a.enabled = false;
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>()) { if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } rb.isKinematic = true; }
            foreach (var c in root.GetComponentsInChildren<Collider>()) if (!IsKept(c.transform)) c.enabled = false;

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (IsKept(r.transform))
                {
                    int keepCount = StripCount(r);
                    if (keepCount >= 0) pieces.Add(new Piece { renderer = r, stripToMaterialCount = keepCount });
                    continue;
                }
                Transform scaled = r is SkinnedMeshRenderer smr && smr.rootBone != null ? smr.rootBone : r.transform;
                var piece = new Piece { renderer = r };
                if (scaledAlready.Add(scaled)) { piece.scaled = scaled; piece.startScale = scaled.localScale; }
                pieces.Add(piece);
            }
        }
        return pieces;
    }

    // How many leading material slots survive, or -1 if nothing on this renderer is stripped.
    // Only trailing slots can go: dropping a slot makes Unity stop drawing that sub-mesh.
    private int StripCount(Renderer r)
    {
        if (stripFromKept == null || stripFromKept.Length == 0) return -1;
        var mats = r.sharedMaterials;
        int count = mats.Length;
        while (count > 1 && System.Array.IndexOf(stripFromKept, mats[count - 1]) >= 0) count--;
        return count < mats.Length ? count : -1;
    }

    private static void Strip(Piece p)
    {
        if (p.renderer == null) return;
        var mats = p.renderer.sharedMaterials;
        if (mats.Length <= p.stripToMaterialCount) return;
        System.Array.Resize(ref mats, p.stripToMaterialCount);
        p.renderer.sharedMaterials = mats;
    }

    // Slow roots get their own long window; everything else gets the normal one, shifted later by a random stagger
    private Vector2 PieceWindow(Transform t)
    {
        if (IsUnder(t, slowRoots)) return slowShrinkWindow;
        float offset = Random.Range(0f, stagger);
        return new Vector2(Mathf.Min(shrinkWindow.x + offset, 0.99f), Mathf.Min(shrinkWindow.y + offset, 1f));
    }

    private void PlayStartSounds()
    {
        if (startSounds == null || startSounds.Length == 0) return;
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = startSoundsSpatialBlend;
        foreach (var clip in startSounds) if (clip != null) source.PlayOneShot(clip, startSoundsVolume);
        if (startSoundsStopAfter > 0f) StartCoroutine(FadeOutAndRemove(source));
    }

    // Separate coroutine: the sounds can outlive the evaporation (they fade under the fade to black)
    private IEnumerator FadeOutAndRemove(AudioSource source)
    {
        yield return new WaitForSeconds(startSoundsStopAfter);
        float startVolume = source.volume;
        for (float t = 0f; t < startSoundsFadeSeconds; t += Time.deltaTime)
        {
            source.volume = Mathf.Lerp(startVolume, 0f, t / startSoundsFadeSeconds);
            yield return null;
        }
        source.Stop();
        Destroy(source);
    }

    private bool IsKept(Transform t) => IsUnder(t, keep);

    private static bool IsUnder(Transform t, Transform[] parents)
    {
        if (parents == null) return false;
        foreach (var k in parents) if (k != null && (t == k || t.IsChildOf(k))) return true;
        return false;
    }
}
