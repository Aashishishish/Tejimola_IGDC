using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fade to black -> cutscene -> (back to gameplay | load a scene).
/// Until the real cutscene exists it shows a placeholder card (title + text) over black with optional music.
/// When the cutscene is made in Timeline, drop its PlayableDirector into "Timeline" and the placeholder
/// is skipped automatically; nothing else changes.
/// The fade overlay is built at runtime, so this works on any object without UI setup.
/// </summary>
public class CutsceneTransition : MonoBehaviour
{
    public enum AfterCutscene { ReturnToGameplay, LoadScene }

    [Header("Fade")]
    [SerializeField] private float fadeToBlackSeconds = 1.5f;
    [SerializeField] private float fadeFromBlackSeconds = 1.5f;
    [SerializeField] private Color fadeColor = Color.black;

    [Header("Cutscene")]
    [Tooltip("Optional: the real cutscene. If set, it plays instead of the placeholder card.")]
    [SerializeField] private PlayableDirector timeline;
    [SerializeField] private string placeholderTitle = "CUTSCENE";
    [TextArea(2, 5)]
    [SerializeField] private string placeholderText = "(placeholder)\nThe bottle gourd falls. Tejimola's spirit is set free from the vines.";
    [Tooltip("How long the placeholder card stays up")]
    [SerializeField] private float placeholderSeconds = 6f;
    [SerializeField] private KeyCode skipKey = KeyCode.Space;

    [Header("Audio")]
    [Tooltip("e.g. 'cut scene bgm'")]
    [SerializeField] private AudioClip cutsceneMusic;
    [Range(0f, 1f)] [SerializeField] private float cutsceneMusicVolume = 0.8f;
    [Tooltip("Fade the game BGM out during the cutscene and back in afterwards")]
    [SerializeField] private bool duckGameMusic = true;

    [Header("Player")]
    [Tooltip("Turned off while the screen is black (movement, camera look, weapon...) and back on afterwards")]
    [SerializeField] private Behaviour[] disableDuringCutscene;

    [Header("After")]
    [SerializeField] private AfterCutscene after = AfterCutscene.ReturnToGameplay;
    [Tooltip("Used when After = LoadScene")]
    [SerializeField] private string sceneToLoad;

    public UnityEvent onCutsceneStarted;
    public UnityEvent onCutsceneFinished;

    public bool IsPlaying { get; private set; }

    private Image fadeImage;
    private Text titleText, bodyText, skipText;
    private AudioSource musicSource;
    private float gameMusicVolume = -1f;

    public void Play()
    {
        if (IsPlaying) return;
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        IsPlaying = true;
        BuildOverlay();

        foreach (var b in disableDuringCutscene) if (b != null) b.enabled = false;

        // 1) Fade to black, game music fading with it
        AudioSource bgm = KillAimTarget.SharedBgmSource;
        if (duckGameMusic && bgm != null) gameMusicVolume = bgm.volume;
        yield return Fade(0f, 1f, fadeToBlackSeconds, bgm, 1f, 0f);

        onCutsceneStarted?.Invoke();
        PlayMusic();

        // 2) The cutscene itself
        if (timeline != null)
        {
            timeline.Play();
            // the Timeline is responsible for its own visuals; lift the black so it can be seen
            SetAlpha(fadeImage, 0f);
            while (timeline.state == PlayState.Playing && !Input.GetKeyDown(skipKey)) yield return null;
            timeline.Stop();
            SetAlpha(fadeImage, 1f);
        }
        else
        {
            yield return ShowPlaceholder();
        }

        onCutsceneFinished?.Invoke();

        // 3) Afterwards
        if (after == AfterCutscene.LoadScene && !string.IsNullOrEmpty(sceneToLoad))
        {
            SceneManager.LoadScene(sceneToLoad);
            yield break;
        }

        StopMusic();
        yield return Fade(1f, 0f, fadeFromBlackSeconds, bgm, 0f, 1f);
        foreach (var b in disableDuringCutscene) if (b != null) b.enabled = true;
        fadeImage.canvas.gameObject.SetActive(false);
        IsPlaying = false;
    }

    private IEnumerator ShowPlaceholder()
    {
        titleText.text = placeholderTitle;
        bodyText.text = placeholderText;
        skipText.text = $"Press {skipKey} to skip";

        yield return FadeTexts(0f, 1f, 0.8f);
        for (float t = 0f; t < placeholderSeconds; t += Time.deltaTime)
        {
            if (Input.GetKeyDown(skipKey)) break;
            yield return null;
        }
        yield return FadeTexts(1f, 0f, 0.6f);
    }

    // Fades the overlay alpha; the game BGM goes the opposite way (musicFrom/To are 0..1 of its original volume)
    private IEnumerator Fade(float from, float to, float seconds, AudioSource bgm, float musicFrom, float musicTo)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            SetAlpha(fadeImage, Mathf.Lerp(from, to, k));
            if (duckGameMusic && bgm != null && gameMusicVolume >= 0f)
                bgm.volume = gameMusicVolume * Mathf.Lerp(musicFrom, musicTo, k);
            yield return null;
        }
        SetAlpha(fadeImage, to);
        if (duckGameMusic && bgm != null && gameMusicVolume >= 0f) bgm.volume = gameMusicVolume * musicTo;
    }

    private IEnumerator FadeTexts(float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float a = Mathf.Lerp(from, to, t / seconds);
            SetAlpha(titleText, a); SetAlpha(bodyText, a); SetAlpha(skipText, a * 0.5f);
            yield return null;
        }
        SetAlpha(titleText, to); SetAlpha(bodyText, to); SetAlpha(skipText, to * 0.5f);
    }

    private void PlayMusic()
    {
        if (cutsceneMusic == null) return;
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            musicSource.loop = true;
        }
        musicSource.clip = cutsceneMusic;
        musicSource.volume = cutsceneMusicVolume;
        musicSource.Play();
    }

    private void StopMusic()
    {
        if (musicSource != null) musicSource.Stop();
    }

    // ---------------------------------------------------------------------------------------------
    // Runtime overlay: a top-most canvas with a full-screen image and three lines of text
    // ---------------------------------------------------------------------------------------------
    private void BuildOverlay()
    {
        if (fadeImage != null) { fadeImage.canvas.gameObject.SetActive(true); return; }

        var canvasGo = new GameObject("Cutscene Overlay", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;   // above the damage overlay and task UI
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        fadeImage = new GameObject("Fade", typeof(Image)).GetComponent<Image>();
        fadeImage.transform.SetParent(canvasGo.transform, false);
        Stretch(fadeImage.rectTransform, Vector2.zero, Vector2.one);
        fadeImage.color = fadeColor;
        fadeImage.raycastTarget = false;
        SetAlpha(fadeImage, 0f);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText = MakeText(canvasGo.transform, font, 64, new Vector2(0.1f, 0.55f), new Vector2(0.9f, 0.7f));
        bodyText = MakeText(canvasGo.transform, font, 34, new Vector2(0.15f, 0.35f), new Vector2(0.85f, 0.55f));
        skipText = MakeText(canvasGo.transform, font, 24, new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.12f));
    }

    private static Text MakeText(Transform parent, Font font, int size, Vector2 min, Vector2 max)
    {
        var t = new GameObject("Text", typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        Stretch(t.rectTransform, min, max);
        t.font = font;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(0.85f, 0.8f, 0.75f, 0f);
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void SetAlpha(Graphic g, float a)
    {
        if (g == null) return;
        var c = g.color; c.a = a; g.color = c;
    }
}
