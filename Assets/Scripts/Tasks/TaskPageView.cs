using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the task list as text lines on a paper page. Completed tasks turn red and get a red brush line drawn through them.
/// Used twice: the small page in the corner and the big page in the pause overlay (only size / font differ).
/// </summary>
public class TaskPageView : MonoBehaviour
{
    [Tooltip("Empty object inside the page where the lines are created")]
    [SerializeField] private RectTransform linesRoot;
    [SerializeField] private Font font;
    [SerializeField] private int fontSize = 28;
    [SerializeField] private float lineHeight = 46f;
    [SerializeField] private Color normalColor = new Color(0.10f, 0.08f, 0.08f, 1f);
    [SerializeField] private Color doneColor = new Color(0.52f, 0.06f, 0.07f, 1f);
    [Tooltip("Brush stroke drawn through a completed task")]
    [SerializeField] private Sprite strikeSprite;
    [SerializeField] private float strikeSeconds = 0.5f;

    private class Line
    {
        public Text text;
        public Image strike;
        public bool done;
        public bool animating;
        public bool pendingReveal;
        public float time;
    }

    private readonly List<Line> lines = new List<Line>();

    public void Refresh(IReadOnlyList<GameTask> tasks, string animateId)
    {
        if (linesRoot == null) return;

        while (lines.Count < tasks.Count) lines.Add(CreateLine(lines.Count));

        for (int i = 0; i < lines.Count; i++)
        {
            bool exists = i < tasks.Count;
            lines[i].text.gameObject.SetActive(exists);
            if (!exists) continue;

            var task = tasks[i];
            var line = lines[i];
            line.text.text = task.text;
            line.text.color = task.completed ? doneColor : normalColor;

            // size the strike to the text
            var srt = line.strike.rectTransform;
            srt.sizeDelta = new Vector2(line.text.preferredWidth + 24f, fontSize * 0.3f);
            line.strike.color = doneColor;

            if (task.completed && !line.done)
            {
                line.done = true;
                // crossed out quietly; the stroke is drawn when the page is next seen
                bool animate = task.id == animateId;
                line.time = 0f;
                line.strike.gameObject.SetActive(true);
                if (animate && isActiveAndEnabled) { line.animating = true; line.strike.fillAmount = 0f; }
                else if (animate) { line.pendingReveal = true; line.strike.fillAmount = 0f; }
                else line.strike.fillAmount = 1f;
            }
            else if (!task.completed)
            {
                line.done = false;
                line.animating = false;
                line.pendingReveal = false;
                line.strike.gameObject.SetActive(false);
            }
        }
    }

    private Line CreateLine(int index)
    {
        var go = new GameObject("Task " + (index + 1), typeof(RectTransform));
        go.transform.SetParent(linesRoot, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(0f, lineHeight);
        rt.anchoredPosition = new Vector2(0f, -index * lineHeight);

        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        var sgo = new GameObject("Strike", typeof(RectTransform));
        sgo.transform.SetParent(go.transform, false);
        var srt = (RectTransform)sgo.transform;
        srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
        srt.pivot = new Vector2(0f, 0.5f);
        srt.anchoredPosition = new Vector2(-10f, fontSize * 0.06f);
        var strike = sgo.AddComponent<Image>();
        strike.sprite = strikeSprite;
        strike.type = Image.Type.Filled;
        strike.fillMethod = Image.FillMethod.Horizontal;
        strike.fillOrigin = (int)Image.OriginHorizontal.Left;
        strike.raycastTarget = false;
        sgo.SetActive(false);

        return new Line { text = text, strike = strike };
    }

    private void OnEnable()
    {
        foreach (var l in lines)
            if (l.pendingReveal) { l.pendingReveal = false; l.animating = true; l.time = -0.4f; }
    }

    private void Update()
    {
        // the brush line is drawn from left to right (unscaled time, so it also works while the game is paused)
        foreach (var l in lines)
        {
            if (!l.animating) continue;
            l.time += Time.unscaledDeltaTime;
            l.strike.fillAmount = Mathf.Clamp01(l.time / strikeSeconds);
            if (l.time >= strikeSeconds) l.animating = false;
        }
    }
}
