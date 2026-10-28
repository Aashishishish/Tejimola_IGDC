using UnityEngine;

/// <summary>
/// Ambient water sound (e.g. 'water without player') for the water around the lake.
/// A 3D looping AudioSource that always sits on the point of the water closest to the listener, so the water sounds
/// louder as you walk towards it, fades away as you leave, and is full volume when you are over / in the water.
/// Put it on the water object (needs a Renderer or Collider to measure the water's size).
/// </summary>
public class WaterAmbience : MonoBehaviour
{
    [SerializeField] private AudioClip waterClip;
    [Range(0f, 1f)] [SerializeField] private float volume = 0.5f;
    [Tooltip("Within this distance of the water the sound is at full volume")]
    [SerializeField] private float minDistance = 4f;
    [Tooltip("Beyond this distance of the water you can't hear it")]
    [SerializeField] private float maxDistance = 45f;
    [Tooltip("Seconds to fade in when the game starts")]
    [SerializeField] private float fadeInSeconds = 3f;

    private AudioSource source;
    private Bounds waterBounds;
    private bool hasBounds;
    private Transform listener;

    private void Start()
    {
        // size of the water
        var r = GetComponentInChildren<Renderer>();
        var c = GetComponentInChildren<Collider>();
        if (r != null) { waterBounds = r.bounds; hasBounds = true; }
        else if (c != null) { waterBounds = c.bounds; hasBounds = true; }

        var go = new GameObject("Water Ambience (3D)");
        go.transform.SetParent(transform, false);
        source = go.AddComponent<AudioSource>();
        source.clip = waterClip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.volume = fadeInSeconds > 0f ? 0f : volume;
        if (waterClip != null) { source.time = Random.value * waterClip.length; source.Play(); }
    }

    private void Update()
    {
        if (source == null) return;

        if (listener == null)
        {
            var l = FindAnyObjectByType<AudioListener>();
            if (l != null) listener = l.transform;
        }

        if (listener != null && hasBounds)
        {
            // closest point of the water to you (or you yourself when you are over the water)
            Vector3 p = waterBounds.ClosestPoint(listener.position);
            source.transform.position = p;
        }

        if (source.volume < volume)
            source.volume = Mathf.MoveTowards(source.volume, volume, (fadeInSeconds > 0f ? volume / fadeInSeconds : volume) * Time.deltaTime);
    }
}
