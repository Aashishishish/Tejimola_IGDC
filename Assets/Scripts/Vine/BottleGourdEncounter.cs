using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Level 1 flow: the gourd is guarded. While any guard vine is alive the gourd is Locked (hits only shake it).
/// Beat every vine -> the gourd unlocks -> cut it down -> encounter complete.
/// Put this on "Bottle Gourd & Main Vines". It only wires existing pieces together, so vines or the gourd
/// can be swapped, added or removed in the Inspector without touching code.
/// </summary>
public class BottleGourdEncounter : MonoBehaviour
{
    public enum State { Guarded, GourdExposed, Complete }

    [Header("Pieces")]
    [Tooltip("Every vine that must be beaten before the gourd can be hit")]
    [SerializeField] private VineHealth[] guardVines;
    [SerializeField] private KillAimTarget gourd;

    [Header("When the gourd drops")]
    [Tooltip("Vines that stop animating and fall to the ground when the gourd is cut down (the NPC vines)")]
    [SerializeField] private VineCollapse[] collapseWhenGourdDrops;
    [Tooltip("Each vine waits a random 0..this many seconds, so they don't all drop on the same frame")]
    [SerializeField] private float collapseStagger = 0.8f;

    [Header("Finale: evaporate, then cutscene")]
    [Tooltip("Dissolves the whole reincarnated form into blood flakes")]
    [SerializeField] private EvaporateEffect evaporate;
    [Tooltip("Seconds after the gourd drops before evaporating (lets the gourd land and the vines collapse)")]
    [SerializeField] private float evaporateDelay = 2.5f;
    [SerializeField] private CutsceneTransition cutscene;
    [Tooltip("0..1 of the evaporation: when the fade to black starts (before 1 = overlaps the end of the flakes)")]
    [Range(0f, 1f)] [SerializeField] private float cutsceneStartsAt = 0.75f;
    [Tooltip("Extra seconds of quiet after the evaporation before the fade starts, so the player can take it in")]
    [SerializeField] private float holdBeforeFade = 0f;

    [Header("Feedback")]
    [Tooltip("Optional: played at the gourd when the last vine is beaten")]
    [SerializeField] private AudioClip gourdExposedSound;
    [Range(0f, 1f)] [SerializeField] private float gourdExposedVolume = 1f;

    [Header("Events (hook up lights, tasks, cutscenes...)")]
    public UnityEvent onGourdExposed;
    public UnityEvent onEncounterComplete;

    public State CurrentState { get; private set; } = State.Guarded;

    private void Start()
    {
        if (gourd == null)
        {
            Debug.LogError("[BottleGourdEncounter] No gourd assigned.", this);
            enabled = false;
            return;
        }

        foreach (var v in guardVines) if (v != null) v.Defeated += OnVineDefeated;
        gourd.Defeated += OnGourdDefeated;

        gourd.Locked = AliveVineCount() > 0;
        if (!gourd.Locked) ExposeGourd();
    }

    private void OnDestroy()
    {
        if (guardVines != null) foreach (var v in guardVines) if (v != null) v.Defeated -= OnVineDefeated;
        if (gourd != null) gourd.Defeated -= OnGourdDefeated;
    }

    private int AliveVineCount()
    {
        int alive = 0;
        if (guardVines != null) foreach (var v in guardVines) if (v != null && !v.IsDefeated) alive++;
        return alive;
    }

    private void OnVineDefeated(VineHealth vine)
    {
        int alive = AliveVineCount();
        Debug.Log($"[BottleGourdEncounter] '{vine.name}' beaten, {alive} guard vine(s) left.", this);
        if (alive == 0 && CurrentState == State.Guarded) ExposeGourd();
    }

    private void ExposeGourd()
    {
        CurrentState = State.GourdExposed;
        gourd.Locked = false;
        if (gourdExposedSound != null)
            AudioSource.PlayClipAtPoint(gourdExposedSound, gourd.transform.position, gourdExposedVolume);
        Debug.Log("[BottleGourdEncounter] All guard vines beaten: the gourd can now be cut down.", this);
        onGourdExposed?.Invoke();
    }

    private void OnGourdDefeated(KillAimTarget target)
    {
        CurrentState = State.Complete;
        Debug.Log("[BottleGourdEncounter] Gourd cut down: encounter complete.", this);

        if (collapseWhenGourdDrops != null)
            foreach (var v in collapseWhenGourdDrops)
                if (v != null) StartCoroutine(CollapseAfter(v, Random.Range(0f, collapseStagger)));

        onEncounterComplete?.Invoke();
        StartCoroutine(FinaleRoutine());
    }

    private System.Collections.IEnumerator FinaleRoutine()
    {
        yield return new WaitForSeconds(evaporateDelay);

        float evaporateTime = 0f;
        if (evaporate != null)
        {
            evaporate.Play();
            evaporateTime = evaporate.Duration * cutsceneStartsAt;
        }
        yield return new WaitForSeconds(evaporateTime + holdBeforeFade);

        if (cutscene != null) cutscene.Play();
    }

    // Each vine topples away from the gourd, as if whatever held it up has died
    private System.Collections.IEnumerator CollapseAfter(VineCollapse vine, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Vector3 away = vine.transform.position - gourd.transform.position;
        vine.Collapse(away);
    }
}
