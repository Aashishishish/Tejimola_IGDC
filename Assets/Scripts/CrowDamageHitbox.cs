using UnityEngine;

/// <summary>
/// Attach to each crow GameObject alongside a Trigger Collider.
/// Calls TakeHit() on the player only when this crow is actively Diving —
/// so crows circling overhead never accidentally damage the player.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CrowDamageHitbox : MonoBehaviour
{
    private CrowAgent crowAgent;

    private void Awake()
    {
        crowAgent = GetComponent<CrowAgent>();

        // Make sure the collider is set to Trigger automatically
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        if (crowAgent == null)
            Debug.LogWarning($"[CrowDamageHitbox] No CrowAgent found on '{gameObject.name}'. Damage will not be state-gated.");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // Only deal damage while this crow is mid-dive
        if (crowAgent != null && crowAgent.CurrentState != CrowAgent.State.Diving) return;

        PlayerHitDeath playerHealth = other.GetComponentInParent<PlayerHitDeath>();
        if (playerHealth == null)
            playerHealth = other.GetComponentInChildren<PlayerHitDeath>();

        if (playerHealth != null)
        {
            playerHealth.TakeHit();
            Debug.Log($"[CrowDamageHitbox] '{gameObject.name}' hit the player during a dive!");
        }
        else
        {
            Debug.LogWarning($"[CrowDamageHitbox] Hit player collider '{other.name}' but could not find PlayerHitDeath.");
        }
    }
}
