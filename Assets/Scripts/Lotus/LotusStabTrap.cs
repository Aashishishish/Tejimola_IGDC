using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LotusStabTrap : MonoBehaviour
{
    [Header("1. Trigger & Model Setup")]
    public Collider triggerAreaCollider;     // Drag 'lily Trigger' here
    public Animator lotusAnimator;          // Drag 'lotus bud swallow' here
    public string attackBoolName = "Stab";  // Animator Bool parameter

    [Header("2. Trap Settings")]
    public float stabInterval = 1.0f;       // Seconds between stabs while the player is in range
    public int hitsToDropFloor = 4;      // On this hit the Water Walk floor disappears

    [Header("3. Floor Reference")]
    public GameObject waterWalkObj;         // 'Water Walk' floor plane

    [Header("5. Sounds")]
    [Tooltip("Played when the lily rises to attack (e.g. 'lilies emerging')")]
    public AudioClip emergeSound;
    [Range(0f, 1f)] public float emergeVolume = 0.9f;

    [Header("4. Runtime Debug")]
    public int lotusHits = 0;
    public bool floorDropped = false;
    public PlayerHitDeath playerHealth;

    private readonly HashSet<Collider> playerCollidersInside = new HashSet<Collider>();
    private Coroutine stabRoutine;

    public bool PlayerInRange => playerCollidersInside.Count > 0;

    private void Start()
    {
        if (waterWalkObj == null) waterWalkObj = GameObject.Find("Water Walk");

        // Match the Animator parameter name regardless of case ("stab" vs "Stab")
        if (lotusAnimator != null)
        {
            bool found = false;
            foreach (AnimatorControllerParameter p in lotusAnimator.parameters)
            {
                if (string.Equals(p.name, attackBoolName, System.StringComparison.OrdinalIgnoreCase))
                {
                    attackBoolName = p.name;
                    found = true;
                    break;
                }
            }
            if (!found) Debug.LogError($"[LotusStabTrap] Animator on '{lotusAnimator.name}' has no parameter '{attackBoolName}'");
        }

        if (triggerAreaCollider != null)
        {
            LotusTriggerAreaBridge bridge = triggerAreaCollider.gameObject.GetComponent<LotusTriggerAreaBridge>();
            if (bridge == null) bridge = triggerAreaCollider.gameObject.AddComponent<LotusTriggerAreaBridge>();
            bridge.mainTrapScript = this;
        }
    }

    private static bool IsPlayer(Collider other)
    {
        return other.CompareTag("Player") ||
               (other.transform.root != null && other.transform.root.CompareTag("Player"));
    }

    public void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;

        playerCollidersInside.Add(other);
        // Use the same health component the vine/crow hitboxes use (on PlayerArmature)
        if (playerHealth == null) playerHealth = other.GetComponentInParent<PlayerHitDeath>();

        if (!floorDropped && stabRoutine == null) stabRoutine = StartCoroutine(StabWhileInRange());
    }

    public void OnTriggerExit(Collider other)
    {
        playerCollidersInside.Remove(other);
    }

    // The lily keeps stabbing while the player stays in range. Player movement is never frozen.
    private IEnumerator StabWhileInRange()
    {
        // the lily rises: play its emerging sound at the lily
        if (emergeSound != null)
            AudioSource.PlayClipAtPoint(emergeSound, lotusAnimator != null ? lotusAnimator.transform.position : transform.position, emergeVolume);

        SetAttacking(true);

        while (PlayerInRange && !floorDropped)
        {
            lotusHits++;
            if (playerHealth != null) playerHealth.TakeHit();
            Debug.Log($"[LotusStabTrap] Lily hit {lotusHits}/{hitsToDropFloor}");

            if (lotusHits >= hitsToDropFloor)
            {
                DropFloor();
                break;
            }

            yield return new WaitForSeconds(stabInterval);
            playerCollidersInside.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        }

        SetAttacking(false);
        stabRoutine = null;
    }

    // Water Walk disappears, the player falls with normal gravity into the WaterDeathZone below
    private void DropFloor()
    {
        floorDropped = true;
        if (waterWalkObj != null) waterWalkObj.SetActive(false);
        Debug.Log($"[LotusStabTrap] Hit {lotusHits} - Water Walk disabled, player falls");
    }

    private void SetAttacking(bool attacking)
    {
        if (lotusAnimator != null) lotusAnimator.SetBool(attackBoolName, attacking); // "Stab" is a Bool parameter
    }

    public void ResetTrap()
    {
        if (stabRoutine != null) StopCoroutine(stabRoutine);
        stabRoutine = null;
        playerCollidersInside.Clear();
        lotusHits = 0;
        floorDropped = false;
        if (waterWalkObj != null) waterWalkObj.SetActive(true);
        SetAttacking(false);
    }
}

public class LotusTriggerAreaBridge : MonoBehaviour
{
    public LotusStabTrap mainTrapScript;

    private void OnTriggerEnter(Collider other)
    {
        if (mainTrapScript != null) mainTrapScript.OnTriggerEnter(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (mainTrapScript != null) mainTrapScript.OnTriggerExit(other);
    }
}
