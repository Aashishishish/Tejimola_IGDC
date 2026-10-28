using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class UIGifAnimator : MonoBehaviour
{
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float framesPerSecond = 4f;

    private Image uiImage;
    private int currentFrameIndex;
    private float timer;

    private void Awake()
    {
        uiImage = GetComponent<Image>();
    }

    private void OnEnable()
    {
        currentFrameIndex = 0;
        timer = 0f;
        if (frames != null && frames.Length > 0)
        {
            uiImage.sprite = frames[0];
        }
    }

    private void Update()
    {
        if (frames == null || frames.Length == 0) return;

        timer += Time.deltaTime;
        if (timer >= 1f / framesPerSecond)
        {
            timer -= 1f / framesPerSecond;
            currentFrameIndex = (currentFrameIndex + 1) % frames.Length;
            uiImage.sprite = frames[currentFrameIndex];
        }
    }
}