using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game overlays that pause the game:
///   T   = task page (also the small paper icon in the corner)
///   P   = pause (also the II button)
///   Esc = game menu (Resume / Main Menu)
/// Press the same key again (or Esc) to close.
/// </summary>
public class TaskPageController : MonoBehaviour
{
    public enum Mode { None, Tasks, Pause, Menu }

    [Header("UI")]
    [SerializeField] private GameObject smallPage;
    [Tooltip("Dim backdrop + big task page")]
    [SerializeField] private GameObject pauseOverlay;
    [Tooltip("Dim backdrop + 'PAUSED' text")]
    [SerializeField] private GameObject pausedPanel;
    [Tooltip("Dim backdrop + Resume / Main Menu buttons")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private Button pauseButton;
    [Tooltip("The small paper icon in the corner: opens the task page")]
    [SerializeField] private Button openButton;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button mainMenuButton;
    [Tooltip("The two bars on the pause button")]
    [SerializeField] private GameObject barsIcon;

    [Header("Keys")]
    [SerializeField] private KeyCode taskKey = KeyCode.T;
    [SerializeField] private KeyCode pauseKey = KeyCode.P;
    [SerializeField] private KeyCode menuKey = KeyCode.Escape;
    [Tooltip("Scene loaded by 'Main Menu'")]
    [SerializeField] private string mainMenuScene = "Home Page";

    [Header("Sounds")]
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;
    [SerializeField] private AudioClip clickSound;
    [Range(0f, 1f)] [SerializeField] private float volume = 0.6f;
    [Tooltip("The page sounds are cut off after this many seconds")]
    [SerializeField] private float maxSoundSeconds = 1.6f;

    [Header("Disabled while paused (restored on resume)")]
    [SerializeField] private Behaviour[] disableWhilePaused;

    private AudioSource sfx;
    private float soundStopTime;
    private Mode mode = Mode.None;
    private bool[] wasEnabled;
    private CursorLockMode prevLock;
    private bool prevCursorVisible;
    private float prevTimeScale = 1f;

    public bool IsPaused => mode != Mode.None;

    private void Awake()
    {
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;
        sfx.ignoreListenerPause = true;

        if (pauseButton != null) pauseButton.onClick.AddListener(() => Toggle(Mode.Pause));
        if (openButton != null) openButton.onClick.AddListener(() => Toggle(Mode.Tasks));
        if (resumeButton != null) resumeButton.onClick.AddListener(() => SetMode(Mode.None, true));
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
        ApplyVisuals();
        if (smallPage != null) smallPage.SetActive(true);
    }

    private void Update()
    {
        if (Input.GetKeyDown(taskKey)) Toggle(Mode.Tasks);
        else if (Input.GetKeyDown(pauseKey)) Toggle(Mode.Pause);
        else if (Input.GetKeyDown(menuKey))
        {
            // Esc closes the task page / pause; otherwise it opens or closes the game menu
            if (mode == Mode.Tasks || mode == Mode.Pause) SetMode(Mode.None, true);
            else Toggle(Mode.Menu);
        }

        if (sfx.isPlaying && Time.unscaledTime > soundStopTime) sfx.Stop();
    }

    private void OnDisable()
    {
        if (mode != Mode.None) SetMode(Mode.None, false);
    }

    public void Toggle() { Toggle(Mode.Tasks); }

    public void Toggle(Mode target)
    {
        SetMode(mode == target ? Mode.None : target, true);
    }

    private void GoToMainMenu()
    {
        SetMode(Mode.None, false);
        SceneManager.LoadScene(mainMenuScene);
    }

    public void SetMode(Mode value, bool playSound)
    {
        if (mode == value) return;
        bool wasPaused = mode != Mode.None;
        mode = value;
        bool nowPaused = mode != Mode.None;

        if (playSound && wasPaused != nowPaused)
        {
            var clip = nowPaused ? openSound : closeSound;
            if (clip == null) clip = clickSound;
            if (clip != null)
            {
                sfx.Stop();
                sfx.clip = clip;
                sfx.volume = volume;
                sfx.Play();
                soundStopTime = Time.unscaledTime + maxSoundSeconds;
            }
        }

        if (!wasPaused && nowPaused)
        {
            prevTimeScale = Time.timeScale;
            prevLock = Cursor.lockState;
            prevCursorVisible = Cursor.visible;

            wasEnabled = new bool[disableWhilePaused != null ? disableWhilePaused.Length : 0];
            for (int i = 0; i < wasEnabled.Length; i++)
            {
                if (disableWhilePaused[i] == null) continue;
                wasEnabled[i] = disableWhilePaused[i].enabled;
                disableWhilePaused[i].enabled = false;
            }

            Time.timeScale = 0f;
            AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (wasPaused && !nowPaused)
        {
            Time.timeScale = prevTimeScale <= 0f ? 1f : prevTimeScale;
            AudioListener.pause = false;
            Cursor.lockState = prevLock;
            Cursor.visible = prevCursorVisible;

            if (wasEnabled != null)
                for (int i = 0; i < wasEnabled.Length; i++)
                    if (disableWhilePaused[i] != null) disableWhilePaused[i].enabled = wasEnabled[i];
        }

        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        if (pauseOverlay != null) pauseOverlay.SetActive(mode == Mode.Tasks);
        if (pausedPanel != null) pausedPanel.SetActive(mode == Mode.Pause);
        if (menuPanel != null) menuPanel.SetActive(mode == Mode.Menu);
    }
}
