using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Homepage menu: Resume / Start / Exit / Controls.
///   Up / Down arrows (or W / S) move the selection. The selected item turns into CAPITAL LETTERS and gets the red underline.
///   Enter / Space (or a mouse click) chooses it.
/// Plays the homepage music, and a click sound when you move or choose.
/// </summary>
public class HomepageMenu : MonoBehaviour
{
    public enum MenuAction { Resume, Start, Exit, Controls }

    [System.Serializable]
    public class Entry
    {
        public string label = "Start";
        public MenuAction action = MenuAction.Start;
        public Text text;
    }

    [Header("Menu")]
    [SerializeField] private Entry[] entries;
    [Tooltip("The red brush line that sits under the selected item")]
    [SerializeField] private RectTransform underline;
    [SerializeField] private float underlineDrop = 40f;        // how far below the text centre the line sits
    [SerializeField] private float underlineExtraWidth = 50f;  // line is this much wider than the text
    [SerializeField] private Color textColor = Color.white;

    [Header("Scenes")]
    [Tooltip("Scene loaded by Start (and by Resume when there is no saved scene). Must be in Build Settings.")]
    [SerializeField] private string gameScene = "Tejimola_master_level";

    [Header("Sound")]
    [SerializeField] private AudioClip homepageMusic;
    [SerializeField] private AudioClip clickSound;
    [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float clickVolume = 0.7f;

    [Header("Game BGM on the home screen (plays quietly under the song, looping)")]
    [SerializeField] private AudioClip gameBgm;
    [Range(0f, 1f)] [SerializeField] private float gameBgmVolume = 0.35f;
    [SerializeField] private float gameBgmFadeIn = 3f;

    [Header("Music Section (plays only this part of the song, then loops it)")]
    [Tooltip("Seconds into the song where the section starts (tejimola og: the part where she says she is Tejimola)")]
    [SerializeField] private float musicStartTime = 22f;
    [Tooltip("Seconds into the song where the section ends. 0 = the end of the clip.")]
    [SerializeField] private float musicEndTime = 0f;
    [SerializeField] private float musicFadeIn = 1.5f;
    [SerializeField] private float musicFadeOut = 2f;
    [Tooltip("Silence between one play of the section and the next")]
    [SerializeField] private float musicLoopGap = 1.5f;

    [Header("Controls Page")]
    [Tooltip("The page that lists the game controls (dark tint + stacked text lines)")]
    [SerializeField] private GameObject controlsPanel;
    [Tooltip("The game name image. It is hidden while the controls page is open.")]
    [SerializeField] private GameObject titleObject;

    private const string LastSceneKey = "LastScene";

    private int selected = -1;          // nothing selected until you press an arrow key (or move the mouse over an item)
    private bool controlsOpen;
    private bool loading;
    private AudioSource musicSource;
    private AudioSource sfxSource;
    private AudioSource gameBgmSource;
    private Coroutine musicRoutine;
    private Vector3 lastMouse;

    private void Awake()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.clip = homepageMusic;
        musicSource.loop = false;          // the section is looped by MusicRoutine, with fades
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.volume = 0f;

        gameBgmSource = gameObject.AddComponent<AudioSource>();
        gameBgmSource.clip = gameBgm;
        gameBgmSource.loop = true;
        gameBgmSource.playOnAwake = false;
        gameBgmSource.spatialBlend = 0f;
        gameBgmSource.volume = 0f;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
    }

    private void Start()
    {
        if (homepageMusic != null) musicRoutine = StartCoroutine(MusicRoutine());
        if (gameBgm != null) StartCoroutine(GameBgmFadeIn());
        if (controlsPanel != null) controlsPanel.SetActive(false);
        lastMouse = Input.mousePosition;
        Refresh();
    }

    private void Update()
    {
        if (loading) return;

        if (controlsOpen) { UpdateControlsPage(); return; }

        // keyboard: arrow keys or W/S
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) Move(+1);
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) Move(-1);

        // mouse: hover selects, click chooses
        if (Input.mousePosition != lastMouse)
        {
            lastMouse = Input.mousePosition;
            int hover = EntryUnderMouse();
            if (hover >= 0 && hover != selected) Select(hover);
        }

        bool choose = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
        if (Input.GetMouseButtonDown(0) && EntryUnderMouse() >= 0) choose = true;
        if (choose && selected >= 0) Activate(selected);
    }

    // ------------------------------------------------------------------ selection

    private void Move(int direction)
    {
        if (entries == null || entries.Length == 0) return;

        int next = selected < 0 ? (direction > 0 ? 0 : entries.Length - 1) : (selected + direction + entries.Length) % entries.Length;
        Select(next);
    }

    private void Select(int index)
    {
        selected = index;
        PlayClick();
        Refresh();
    }

    // Selected item: CAPITAL LETTERS + red underline. Everything else: normal.
    private void Refresh()
    {
        if (entries == null) return;

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].text == null) continue;
            entries[i].text.text = i == selected ? entries[i].label.ToUpper() : entries[i].label;
            entries[i].text.color = textColor;
        }

        if (underline == null) return;

        bool show = selected >= 0 && entries[selected].text != null;
        underline.gameObject.SetActive(show);
        if (!show) return;

        var t = entries[selected].text;
        // make sure the text has been laid out for the new letters before measuring it
        Canvas.ForceUpdateCanvases();
        var rt = t.rectTransform;
        underline.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - underlineDrop);
        underline.sizeDelta = new Vector2(t.preferredWidth + underlineExtraWidth, underline.sizeDelta.y);
    }

    private int EntryUnderMouse()
    {
        if (entries == null) return -1;
        for (int i = 0; i < entries.Length; i++)
        {
            var t = entries[i].text;
            if (t == null) continue;
            // only the part of the item that has text counts, not the whole wide rectangle
            var rt = t.rectTransform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, null, out local)) continue;
            float halfW = t.preferredWidth * 0.5f + 20f;
            if (Mathf.Abs(local.x) <= halfW && Mathf.Abs(local.y) <= rt.rect.height * 0.5f) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------ actions

    private void Activate(int index)
    {
        PlayClick();

        switch (entries[index].action)
        {
            case MenuAction.Start:
                PlayerPrefs.SetString(LastSceneKey, gameScene);
                PlayerPrefs.Save();
                StartCoroutine(LoadScene(gameScene));
                break;

            case MenuAction.Resume:
                string last = PlayerPrefs.GetString(LastSceneKey, gameScene);
                if (!Application.CanStreamedLevelBeLoaded(last)) last = gameScene;
                StartCoroutine(LoadScene(last));
                break;

            case MenuAction.Controls:
                OpenControls();
                break;

            case MenuAction.Exit:
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;
        }
    }

    private IEnumerator LoadScene(string sceneName)
    {
        loading = true;

        // fade the homepage music out before the game starts
        if (musicRoutine != null) StopCoroutine(musicRoutine);
        float startVolume = musicSource.volume;
        float startBgm = gameBgmSource.volume;
        const float fade = 0.8f;
        for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(startVolume, 0f, t / fade);
            gameBgmSource.volume = Mathf.Lerp(startBgm, 0f, t / fade);
            yield return null;
        }
        musicSource.volume = 0f;
        gameBgmSource.volume = 0f;

        SceneManager.LoadScene(sceneName);
    }

    // The game BGM loops quietly under the song on the home screen
    private IEnumerator GameBgmFadeIn()
    {
        gameBgmSource.Play();
        for (float t = 0f; t < gameBgmFadeIn; t += Time.unscaledDeltaTime)
        {
            gameBgmSource.volume = gameBgmVolume * (t / gameBgmFadeIn);
            yield return null;
        }
        gameBgmSource.volume = gameBgmVolume;
    }

    // ------------------------------------------------------------------ music section

    // Plays only [musicStartTime, musicEndTime] of the song: fade in, play, fade out, short gap, repeat.
    private IEnumerator MusicRoutine()
    {
        float clipEnd = homepageMusic.length;
        float start = Mathf.Clamp(musicStartTime, 0f, Mathf.Max(0f, clipEnd - 1f));
        float end = musicEndTime > start ? Mathf.Min(musicEndTime, clipEnd) : clipEnd;
        float length = end - start;
        float fadeIn = Mathf.Min(musicFadeIn, length * 0.4f);
        float fadeOut = Mathf.Min(musicFadeOut, length * 0.4f);

        while (true)
        {
            musicSource.time = start;
            musicSource.volume = 0f;
            musicSource.Play();

            // fade in
            for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
            {
                musicSource.volume = musicVolume * (t / fadeIn);
                yield return null;
            }
            musicSource.volume = musicVolume;

            // play until it is time to fade out
            while (musicSource.isPlaying && musicSource.time < end - fadeOut)
                yield return null;

            // fade out
            float from = musicSource.volume;
            for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
            {
                musicSource.volume = Mathf.Lerp(from, 0f, t / fadeOut);
                yield return null;
            }
            musicSource.Stop();

            yield return new WaitForSecondsRealtime(musicLoopGap);
        }
    }

    // ------------------------------------------------------------------ controls page

    private void OpenControls()
    {
        controlsOpen = true;
        SetMenuVisible(false);                                   // the menu items would show through the page
        if (titleObject != null) titleObject.SetActive(false);   // no game name on this page
        if (controlsPanel != null) controlsPanel.SetActive(true);
    }

    private void CloseControls()
    {
        controlsOpen = false;
        if (controlsPanel != null) controlsPanel.SetActive(false);
        if (titleObject != null) titleObject.SetActive(true);
        SetMenuVisible(true);
        PlayClick();
    }

    // Shows / hides the menu items and the red underline while the controls page is open
    private void SetMenuVisible(bool visible)
    {
        if (entries != null && entries.Length > 0 && entries[0].text != null && entries[0].text.transform.parent != null)
            entries[0].text.transform.parent.gameObject.SetActive(visible);

        if (visible) Refresh();                      // puts the underline back under the selected item
        else if (underline != null) underline.gameObject.SetActive(false);
    }

    private void UpdateControlsPage()
    {
        // back to the menu: Esc, Backspace, Enter, Space or a click
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace) ||
            Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
            CloseControls();
    }

    // ------------------------------------------------------------------ sound

    private void PlayClick()
    {
        if (clickSound != null) sfxSource.PlayOneShot(clickSound, clickVolume);
    }
}
