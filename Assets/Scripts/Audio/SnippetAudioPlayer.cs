using System.Collections;
using UnityEngine;

/// <summary>
/// Plays short random snippets of a long clip at random intervals from this object (3D, so it sounds like it comes from here).
/// Used for the crying girl on the bottle gourd. Disabling this component (KillAimTarget does it when the gourd is
/// defeated, via its Trap Script slot) stops the sound immediately.
/// </summary>
public class SnippetAudioPlayer : MonoBehaviour
{
    [Header("Clip")]
    [SerializeField] private AudioClip clip;
    [Tooltip("Optional. If empty, an AudioSource is added to this object automatically.")]
    [SerializeField] private AudioSource source;

    [Header("Timing")]
    [Tooltip("Seconds to wait before the very first snippet")]
    [SerializeField] private float firstDelay = 4f;
    [Tooltip("Random silence between snippets (seconds)")]
    [SerializeField] private float minInterval = 8f;
    [SerializeField] private float maxInterval = 20f;

    [Header("Snippet")]
    [Tooltip("Random snippet length (seconds)")]
    [SerializeField] private float minSnippetLength = 2f;
    [SerializeField] private float maxSnippetLength = 4f;
    [Tooltip("Fade in / fade out time so the cuts don't click")]
    [SerializeField] private float fadeTime = 0.6f;

    [Header("Sound")]
    [Range(0f, 1f)] [SerializeField] private float volume = 0.35f;
    [Tooltip("0 = 2D, 1 = fully 3D (comes from this object's position)")]
    [Range(0f, 1f)] [SerializeField] private float spatialBlend = 1f;
    [SerializeField] private float minDistance = 3f;
    [SerializeField] private float maxDistance = 30f;
    [Tooltip("Skip a snippet if the player is farther than this (0 = always play)")]
    [SerializeField] private float onlyWhenPlayerWithin = 45f;

    private void OnEnable()
    {
        if (clip == null) return;

        if (source == null)
        {
            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
        }

        source.clip = clip;
        source.loop = false;
        source.playOnAwake = false;
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.volume = 0f;

        StartCoroutine(SnippetLoop(firstDelay));
    }

    private void OnDisable()
    {
        if (source != null) source.Stop();
    }

    /// <summary>
    /// Plays a snippet right now (e.g. the girl cries out when the gourd is hit), then carries on with the
    /// random loop. Restarting the loop means the scheduled snippet can't overlap this one on the same source.
    /// </summary>
    public void PlaySnippetNow()
    {
        if (!isActiveAndEnabled || clip == null || source == null) return;
        StopAllCoroutines();
        StartCoroutine(SnippetNowThenLoop());
    }

    private IEnumerator SnippetNowThenLoop()
    {
        yield return PlayOneSnippet();
        yield return SnippetLoop(Random.Range(minInterval, maxInterval));
    }

    private IEnumerator SnippetLoop(float delay)
    {
        yield return new WaitForSeconds(delay);

        while (true)
        {
            if (PlayerIsInRange()) yield return PlayOneSnippet();
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
        }
    }

    private IEnumerator PlayOneSnippet()
    {
        float length = Mathf.Min(Random.Range(minSnippetLength, maxSnippetLength), clip.length);
        float start = Random.Range(0f, Mathf.Max(0f, clip.length - length));

        source.time = start;
        source.volume = 0f;
        source.Play();

        // fade in
        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            source.volume = volume * (t / fadeTime);
            yield return null;
        }
        source.volume = volume;

        // hold
        yield return new WaitForSeconds(Mathf.Max(0f, length - 2f * fadeTime));

        // fade out
        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            source.volume = volume * (1f - t / fadeTime);
            yield return null;
        }
        source.Stop();
    }

    private AudioListener listener;

    private bool PlayerIsInRange()
    {
        if (onlyWhenPlayerWithin <= 0f) return true;

        // Cached: FindAnyObjectByType scans the whole scene
        if (listener == null || !listener.isActiveAndEnabled) listener = FindAnyObjectByType<AudioListener>();
        if (listener == null) return true;
        return (listener.transform.position - transform.position).sqrMagnitude <= onlyWhenPlayerWithin * onlyWhenPlayerWithin;
    }
}
