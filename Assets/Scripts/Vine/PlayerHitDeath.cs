using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PlayerHitDeath : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private Image damageOverlay;

    [Header("5-Tier Hit Textures (Project Assets)")]
    [SerializeField] private Sprite[] deathTiers = new Sprite[5]; // Drag 'death 1' through 'death 5' here

    [Header("Death Screen UI (Layered Sprites & Panel)")]
    [SerializeField] private GameObject deathScreenPanel;       // Canvas Panel containing the death UI
    [SerializeField] private Image deathScreenImageComponent;   // The UI Image component inside Death Screen Panel
    [SerializeField] private Sprite[] deathBlindSprites;        // Drag 'death blind' sprites directly from Project
    [SerializeField] private float deathScreenFadeSpeed = 2.0f;  // Speed at which death screen fades in/out
    [SerializeField] private float deathScreenDelay = 2.0f;      // Hold duration on full screen before reload

    [Header("Wake Up / Respawn Settings")]
    [SerializeField] private float wakeUpFadeSpeed = 1.5f;       // Speed at which the death screen fades away after respawn

    [Header("Hit & Decay Settings")]
    [SerializeField] private int maxHits = 5;
    [SerializeField] private float hitDecayInterval = 4.0f;     // Recovers 1 tier every 4 seconds
    [Tooltip("Once the murder of crows has been called, hits heal this slowly (seconds per hit), so 5 crow hits really kill you")]
    [SerializeField] private float hitDecayIntervalDuringMurder = 30f;
    [SerializeField] private float invulnerabilityWindow = 0.5f; // Prevents duplicate hits per frame

    [Header("Fade Speeds")]
    [SerializeField] private float damageFadeSpeed = 3.0f;       // Smooth transition speed for damage overlay hits & recovery
    [SerializeField] private float finalDeathFadeDuration = 1.0f;

    private static bool shouldPlayWakeUp = false; // Preserved across scene reloads

    private int currentHits = 0;
    private float timeSinceLastHit = 0f;
    private float lastHitTimestamp = -10f;
    private bool isDead = false;
    private Color targetOverlayColor = new Color(1f, 1f, 1f, 0f); // Default transparent target

    private void Start()
    {
        // Check if returning from a death scene reload
        if (shouldPlayWakeUp)
        {
            shouldPlayWakeUp = false;
            StartCoroutine(WakeUpSequence());
        }
        else
        {
            // Default initial start state
            if (deathScreenPanel != null)
            {
                deathScreenPanel.SetActive(false);
            }

            if (deathScreenImageComponent != null)
            {
                SetImageAlpha(deathScreenImageComponent, 0f);
            }

            UpdateOverlaySprite(instant: true);
        }
    }

    private void Update()
    {
        if (isDead || damageOverlay == null) return;

        // Smoothly interpolate overlay opacity/color towards target state
        damageOverlay.color = Color.Lerp(damageOverlay.color, targetOverlayColor, Time.deltaTime * damageFadeSpeed);

        // Track decay timer if damaged
        if (currentHits > 0)
        {
            timeSinceLastHit += Time.deltaTime;

            if (timeSinceLastHit >= CurrentDecayInterval())
            {
                currentHits--;
                timeSinceLastHit = 0f;
                Debug.Log("Recovered 1 tier! Current hits remaining: " + currentHits);
                UpdateOverlaySprite(instant: false);
            }
        }
    }

    // After Tejimola has called the murder of crows (transition + phase 2) hits heal much more slowly
    private float CurrentDecayInterval()
    {
        bool murderCalled = CrowManager.Instance != null &&
                            CrowManager.Instance.CurrentPhase != CrowManager.CrowPhase.Phase1_TejimolaSolo;
        return murderCalled ? hitDecayIntervalDuringMurder : hitDecayInterval;
    }

    /// <summary>Returns true if the hit counted (false while dead or invulnerable), so callers can add knockback only on real hits.</summary>
    public bool TakeHit()
    {
        if (isDead) return false;

        // Ignore hits occurring within the invulnerability buffer
        if (Time.time - lastHitTimestamp < invulnerabilityWindow) return false;

        lastHitTimestamp = Time.time;
        timeSinceLastHit = 0f;
        currentHits++;

        Debug.Log("Player hit! Current hits: " + currentHits + "/" + maxHits);
        UpdateOverlaySprite(instant: false);

        if (currentHits >= maxHits)
        {
            isDead = true;
            StartCoroutine(DieAndRestart());
        }
        return true;
    }

    private void UpdateOverlaySprite(bool instant)
    {
        if (damageOverlay == null) return;

        if (currentHits == 0)
        {
            targetOverlayColor = new Color(damageOverlay.color.r, damageOverlay.color.g, damageOverlay.color.b, 0f);
            if (instant)
            {
                damageOverlay.color = targetOverlayColor;
                damageOverlay.enabled = false;
            }
        }
        else
        {
            damageOverlay.enabled = true;
            int spriteIndex = Mathf.Clamp(currentHits - 1, 0, deathTiers.Length - 1);

            if (deathTiers[spriteIndex] != null)
            {
                damageOverlay.sprite = deathTiers[spriteIndex];
            }

            targetOverlayColor = new Color(1f, 1f, 1f, 1f);
            if (instant)
            {
                damageOverlay.color = targetOverlayColor;
            }
        }
    }

    // Darkens the screen to the last tier before death without restarting the scene (checkpoint deaths)
    public void ShowNearDeath()
    {
        if (isDead) return;
        currentHits = maxHits - 1;
        timeSinceLastHit = 0f;
        UpdateOverlaySprite(instant: false);
    }

    // Clears all damage and hides overlays
    public void ResetHealth()
    {
        currentHits = 0;
        timeSinceLastHit = 0f;
        isDead = false;

        if (deathScreenPanel != null)
        {
            deathScreenPanel.SetActive(false);
        }

        if (deathScreenImageComponent != null)
        {
            SetImageAlpha(deathScreenImageComponent, 0f);
        }

        UpdateOverlaySprite(instant: true);
    }

    private IEnumerator DieAndRestart()
    {
        yield return FadeToDeathScreen();

        // 3. Hold death screen before reloading
        yield return new WaitForSeconds(deathScreenDelay);

        // 4. Signal wake-up sequence on next load and reload
        shouldPlayWakeUp = true;
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);
    }

    // Fades the damage overlay to black, then fades in the death screen. Shared by hit deaths and the water death zone.
    public IEnumerator FadeToDeathScreen()
    {
        isDead = true;
        float timer = 0f;
        Color startColor = damageOverlay != null ? damageOverlay.color : Color.white;
        Color black = Color.black;

        // 1. Fade damage overlay smoothly to full black
        while (timer < finalDeathFadeDuration)
        {
            timer += Time.deltaTime;
            if (damageOverlay != null)
            {
                damageOverlay.color = Color.Lerp(startColor, black, timer / finalDeathFadeDuration);
            }
            yield return null;
        }

        if (damageOverlay != null) damageOverlay.color = black;

        // 2. Activate Death Screen Panel & fade in the death blind sprite
        if (deathScreenPanel != null)
        {
            deathScreenPanel.SetActive(true);

            if (deathScreenImageComponent != null && deathBlindSprites != null && deathBlindSprites.Length > 0)
            {
                deathScreenImageComponent.sprite = deathBlindSprites[deathBlindSprites.Length - 1];
            }

            float fadeTimer = 0f;
            while (fadeTimer < 1f)
            {
                fadeTimer += Time.deltaTime * deathScreenFadeSpeed;
                float currentAlpha = Mathf.Clamp01(fadeTimer);

                if (deathScreenImageComponent != null)
                {
                    SetImageAlpha(deathScreenImageComponent, currentAlpha);
                }

                yield return null;
            }
        }
    }

    private IEnumerator WakeUpSequence()
    {
        yield return WakeUpFromDeathScreen();
    }

    // Plays the death screen in reverse (eyes opening) and clears all damage. Also used after a water-death respawn.
    public IEnumerator WakeUpFromDeathScreen()
    {
        // 1. Initialize screen state as fully opaque
        if (damageOverlay != null)
        {
            damageOverlay.enabled = true;
            damageOverlay.color = Color.black;
        }

        if (deathScreenPanel != null)
        {
            deathScreenPanel.SetActive(true);

            if (deathScreenImageComponent != null && deathBlindSprites != null && deathBlindSprites.Length > 0)
            {
                deathScreenImageComponent.sprite = deathBlindSprites[deathBlindSprites.Length - 1];
                SetImageAlpha(deathScreenImageComponent, 1f);
            }
        }

        yield return new WaitForSeconds(0.3f); // Brief pause before opening eyes

        // 2. Fade out death screen UI image (reverse blind)
        float fadeTimer = 1f;
        while (fadeTimer > 0f)
        {
            fadeTimer -= Time.deltaTime * wakeUpFadeSpeed;
            float currentAlpha = Mathf.Clamp01(fadeTimer);

            if (deathScreenImageComponent != null)
            {
                SetImageAlpha(deathScreenImageComponent, currentAlpha);
            }

            yield return null;
        }

        if (deathScreenPanel != null)
        {
            deathScreenPanel.SetActive(false);
        }

        // 3. Fade out black damage overlay to reveal game screen completely
        float overlayTimer = 1f;
        while (overlayTimer > 0f)
        {
            overlayTimer -= Time.deltaTime * wakeUpFadeSpeed;
            float currentAlpha = Mathf.Clamp01(overlayTimer);

            if (damageOverlay != null)
            {
                damageOverlay.color = new Color(0f, 0f, 0f, currentAlpha);
            }

            yield return null;
        }

        // 4. Fully clear health and restore interactive gameplay state
        ResetHealth();
    }

    private void SetImageAlpha(Image img, float alpha)
    {
        if (img != null)
        {
            Color c = img.color;
            c.a = alpha;
            img.color = c;
        }
    }
}