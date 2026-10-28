using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GameTask
{
    public string id = "task";
    [TextArea(1, 2)] public string text = "New task";
    [Tooltip("If set, the task completes when the player picks up an object with this tag (e.g. 'Arriyal weapon')")]
    public string completeOnPickupTag = "";
    public bool completed;
}

/// <summary>
/// The list of tasks shown on the task pages (the small one in the corner and the big pause overlay).
/// Complete a task with GameTasks.Instance.Complete("id"), by a TaskTriggerZone, or by picking up an object with the
/// task's 'Complete On Pickup Tag'. A completed task is crossed out on every page.
/// </summary>
public class GameTasks : MonoBehaviour
{
    public static GameTasks Instance { get; private set; }

    [SerializeField] private List<GameTask> tasks = new List<GameTask>();
    [Tooltip("Every page that shows the tasks")]
    [SerializeField] private List<TaskPageView> pages = new List<TaskPageView>();

    [Header("Sound")]
    [Tooltip("Played when a task is crossed out. Leave empty to use a generated pen-scratch sound.")]
    [SerializeField] private AudioClip crossOutSound;
    [Range(0f, 1f)] [SerializeField] private float crossOutVolume = 0.45f;

    private AudioSource sfx;
    private AudioClip scratch;

    public IReadOnlyList<GameTask> Tasks => tasks;

    private void Awake()
    {
        Instance = this;

        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;
        sfx.ignoreListenerPause = true;      // still audible while the game is paused
        scratch = crossOutSound != null ? crossOutSound : MakeScratchClip();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        RefreshPages(null);
    }

    public bool IsCompleted(string id)
    {
        foreach (var t in tasks) if (t.id == id) return t.completed;
        return false;
    }

    public bool Complete(string id)
    {
        foreach (var t in tasks)
        {
            if (t.id != id) continue;
            if (t.completed) return false;

            t.completed = true;
            Debug.Log($"[GameTasks] Task completed: '{t.text}'");
            RefreshPages(id);
            return true;
        }

        Debug.LogWarning($"[GameTasks] No task with id '{id}'.");
        return false;
    }

    // Called when the player picks something up
    public void NotifyPickup(string pickedUpTag)
    {
        if (string.IsNullOrEmpty(pickedUpTag)) return;
        foreach (var t in tasks)
            if (!t.completed && !string.IsNullOrEmpty(t.completeOnPickupTag) && t.completeOnPickupTag == pickedUpTag)
                Complete(t.id);
    }

    private void RefreshPages(string animateId)
    {
        foreach (var p in pages)
            if (p != null) p.Refresh(tasks, animateId);
    }

    // A short scratchy 'pen on paper' sound, made on the fly so no audio file is needed
    private static AudioClip MakeScratchClip()
    {
        const int rate = 44100;
        int n = (int)(rate * 0.45f);
        var data = new float[n];
        var rnd = new System.Random(11);
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float env = Mathf.Sin(t * Mathf.PI); env *= env;
            float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
            low = low * 0.55f + noise * 0.45f;                       // soft low-pass, so it is not harsh
            float stroke = 0.55f + 0.45f * Mathf.Sin(i * 0.0035f);   // a few back-and-forth strokes
            data[i] = low * env * stroke;
        }
        var clip = AudioClip.Create("pen scratch", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
