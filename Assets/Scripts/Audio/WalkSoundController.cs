using UnityEngine;

/// <summary>
/// Quiet walking sounds on the player (PlayerArmature). Picks the clip by what you're standing on:
///   water floor ("Water Walk")  -> waterWalk
///   inside a house (SafeZone)   -> indoorWalk
///   anywhere else               -> grassWalk
/// It loops softly while you move and fades out when you stop, so it never gets loud or spammy.
/// The Starter Assets footstep sounds on ThirdPersonController should be set to volume 0 so they don't double up.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class WalkSoundController : MonoBehaviour
{
    [Header("Clips")]
    [SerializeField] private AudioClip grassWalk;
    [SerializeField] private AudioClip indoorWalk;
    [SerializeField] private AudioClip waterWalk;

    [Header("Volume (keep low so footsteps stay subtle)")]
    [Range(0f, 1f)] [SerializeField] private float grassVolume = 0.12f;
    [Range(0f, 1f)] [SerializeField] private float indoorVolume = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float waterVolume = 0.15f;

    [Header("Movement")]
    [Tooltip("Below this horizontal speed (m/s) there is no walking sound")]
    [SerializeField] private float minSpeed = 0.3f;
    [Tooltip("At this speed (m/s) the sound reaches its full (low) volume; slower walking is quieter")]
    [SerializeField] private float fullVolumeSpeed = 2f;
    [SerializeField] private float fadeInSpeed = 4f;
    [SerializeField] private float fadeOutSpeed = 8f;

    [Header("Surface Detection")]
    [Tooltip("A floor whose name contains this counts as water")]
    [SerializeField] private string waterSurfaceName = "Water Walk";
    [SerializeField] private LayerMask groundMask = ~0;

    private enum Surface { Grass, Indoor, Water }

    private CharacterController cc;
    private AudioSource source;
    private AudioClip currentClip;
    private Collider[] indoorZones;
    private float lastGroundedTime;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();

        var go = new GameObject("Walk Sounds");
        go.transform.SetParent(transform, false);
        source = go.AddComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;      // 2D, always the same
        source.volume = 0f;
    }

    private void Start()
    {
        // Houses already have SafeZone trigger volumes; treat being inside one as "indoors"
        var zones = FindObjectsByType<SafeZone>(FindObjectsInactive.Exclude);
        indoorZones = new Collider[zones.Length];
        for (int i = 0; i < zones.Length; i++) indoorZones[i] = zones[i].GetComponent<Collider>();
    }

    private void Update()
    {
        if (cc.isGrounded) lastGroundedTime = Time.time;
        bool grounded = cc.isGrounded || Time.time - lastGroundedTime < 0.15f;

        Vector3 v = cc.velocity;
        float speed = new Vector2(v.x, v.z).magnitude;
        bool moving = grounded && speed > minSpeed;

        Surface surface = DetectSurface();
        AudioClip wantedClip = ClipFor(surface);
        float targetVolume = moving ? VolumeFor(surface) * Mathf.Clamp01(speed / Mathf.Max(0.01f, fullVolumeSpeed)) : 0f;

        if (wantedClip == null) targetVolume = 0f;

        // Surface changed: fade out, then swap the clip
        if (wantedClip != currentClip && source.volume > 0.01f)
        {
            source.volume = Mathf.MoveTowards(source.volume, 0f, fadeOutSpeed * Time.deltaTime);
            return;
        }

        if (wantedClip != currentClip)
        {
            currentClip = wantedClip;
            source.clip = wantedClip;
            if (wantedClip != null) source.time = Random.value * wantedClip.length;
            if (source.isPlaying) source.Stop();
        }

        float rate = targetVolume > source.volume ? fadeInSpeed : fadeOutSpeed;
        source.volume = Mathf.MoveTowards(source.volume, targetVolume, rate * Time.deltaTime);

        if (targetVolume > 0f && source.clip != null && !source.isPlaying) source.Play();
        else if (targetVolume <= 0f && source.volume <= 0.001f && source.isPlaying) source.Pause();
    }

    private Surface DetectSurface()
    {
        // Water floor under the feet?
        if (!string.IsNullOrEmpty(waterSurfaceName) &&
            Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.5f, groundMask, QueryTriggerInteraction.Ignore) &&
            hit.collider.name.Contains(waterSurfaceName))
            return Surface.Water;

        // Inside a house?
        if (indoorZones != null)
            for (int i = 0; i < indoorZones.Length; i++)
                if (indoorZones[i] != null && indoorZones[i].bounds.Contains(transform.position + Vector3.up * 0.5f))
                    return Surface.Indoor;

        return Surface.Grass;
    }

    private AudioClip ClipFor(Surface s)
    {
        switch (s)
        {
            case Surface.Water: return waterWalk != null ? waterWalk : grassWalk;
            case Surface.Indoor: return indoorWalk != null ? indoorWalk : grassWalk;
            default: return grassWalk;
        }
    }

    private float VolumeFor(Surface s)
    {
        switch (s)
        {
            case Surface.Water: return waterVolume;
            case Surface.Indoor: return indoorVolume;
            default: return grassVolume;
        }
    }
}
