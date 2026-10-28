using System.Collections;
using UnityEngine;
using Unity.Cinemachine;
using StarterAssets;

public class WaterDeathZone : MonoBehaviour
{
    [Header("Respawn & Timing Setup")]
    public Transform checkpointRespawnPoint; // Drag 'water death checkpoint' here
    public float deathScreenDelay = 1.5f;
    public float maxFallTime = 3.0f;         // Safety limit if the player never lands on the riverbed
    public LotusStabTrap lotusTrapScript;   // Drag 'lotus bud swallow' here to reset floor

    private bool deathActive = false;

    private void Start()
    {
        // Auto-find lotus trap script if unassigned
        if (lotusTrapScript == null)
        {
            lotusTrapScript = FindAnyObjectByType<LotusStabTrap>();
            Debug.Log($"[WaterDeathZone] Auto-found LotusStabTrap: {(lotusTrapScript != null ? "SUCCESS" : "FAILED")}");
        }

        // Auto-find checkpoint if unassigned
        if (checkpointRespawnPoint == null)
        {
            GameObject cp = GameObject.Find("water death checkpoint");
            if (cp != null) checkpointRespawnPoint = cp.transform;
            else Debug.LogError("[WaterDeathZone] FAILED to find 'water death checkpoint'! Drag it manually into Inspector.");
        }

        Collider col = GetComponent<Collider>();
        if (col == null) Debug.LogError("[WaterDeathZone] CRITICAL ERROR: No Collider found on " + gameObject.name + "!");
        else if (!col.isTrigger) Debug.LogWarning("[WaterDeathZone] Collider on " + gameObject.name + " should be a Trigger.");
    }

    // Stay as well as Enter, in case the player is already inside when the floor disappears
    private void OnTriggerEnter(Collider other) { ProcessDeath(other); }
    private void OnTriggerStay(Collider other) { ProcessDeath(other); }

    // Water only kills once the lily has removed the Water Walk floor
    private bool IsArmed()
    {
        if (lotusTrapScript == null) return true;
        if (lotusTrapScript.waterWalkObj != null) return !lotusTrapScript.waterWalkObj.activeInHierarchy;
        return lotusTrapScript.floorDropped;
    }

    private void ProcessDeath(Collider other)
    {
        if (deathActive || !IsArmed()) return;

        bool isPlayer = other.CompareTag("Player") ||
                       (other.transform.root != null && other.transform.root.CompareTag("Player"));
        if (!isPlayer) return;

        deathActive = true;
        Debug.Log($"[WaterDeathZone] Player '{other.name}' fell into the water. Starting death sequence...");
        StartCoroutine(ExecuteDeathSequence(other));
    }

    private IEnumerator ExecuteDeathSequence(Collider playerCollider)
    {
        // Same components the vine/crow hitboxes use (on PlayerArmature)
        PlayerHitDeath playerHealth = playerCollider.GetComponentInParent<PlayerHitDeath>();
        CharacterController cc = playerCollider.GetComponentInParent<CharacterController>();
        ThirdPersonController tpc = playerCollider.GetComponentInParent<ThirdPersonController>();

        // 1. Let the player fall through the water until they land on the riverbed terrain
        float fallTimer = 0f;
        while (fallTimer < maxFallTime)
        {
            fallTimer += Time.deltaTime;
            if (fallTimer > 0.2f && cc != null && cc.isGrounded) break;
            yield return null;
        }
        if (cc != null) Debug.Log($"[WaterDeathZone] Player landed at y={cc.transform.position.y:F2} after {fallTimer:F2}s");

        // 2. Stop player input and play the full death screen (same as dying from hits)
        if (tpc != null) tpc.enabled = false;
        if (playerHealth != null) yield return playerHealth.FadeToDeathScreen();

        yield return new WaitForSeconds(deathScreenDelay);

        // 3. Teleport the body that actually walks (PlayerArmature), not the stationary root
        if (checkpointRespawnPoint != null)
        {
            Transform body = cc != null ? cc.transform : playerCollider.transform.root;
            Vector3 delta = checkpointRespawnPoint.position - body.position;

            if (cc != null) cc.enabled = false;
            body.SetPositionAndRotation(checkpointRespawnPoint.position, checkpointRespawnPoint.rotation);
            Physics.SyncTransforms();
            if (cc != null) cc.enabled = true;

            // Snap the camera instead of letting it drag through the terrain to the checkpoint
            foreach (CinemachineVirtualCameraBase vcam in FindObjectsByType<CinemachineVirtualCameraBase>())
            {
                if (vcam.Follow != null && vcam.Follow.IsChildOf(body)) vcam.OnTargetObjectWarped(vcam.Follow, delta);
            }
        }
        else
        {
            Debug.LogError("[WaterDeathZone] TELEPORT FAILED: checkpointRespawnPoint is NULL!");
        }

        // 4. Reset the lotus trap / Water Walk floor, then play the death screen in reverse (wake up)
        if (lotusTrapScript != null) lotusTrapScript.ResetTrap();
        if (playerHealth != null) yield return playerHealth.WakeUpFromDeathScreen();
        if (tpc != null) tpc.enabled = true;

        deathActive = false;
        Debug.Log("[WaterDeathZone] Respawned at checkpoint.");
    }
}
