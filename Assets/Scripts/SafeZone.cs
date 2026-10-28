using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SafeZone : MonoBehaviour
{
    [Header("Visual Debug")]
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 0f, 0.25f);

    private void Reset()
    {
        // Automatically make sure the collider is set to Trigger
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            CrowManager.Instance?.SetPlayerSafe(true);
            Debug.Log($"<color=lime>[Safe Zone]</color> Player entered safe area: {gameObject.name}");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            CrowManager.Instance?.SetPlayerSafe(false);
            Debug.Log($"<color=orange>[Safe Zone]</color> Player exited safe area: {gameObject.name}");
        }
    }

    private void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = gizmoColor;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (col is BoxCollider box)
        {
            Gizmos.DrawCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.DrawSphere(sphere.center, sphere.radius);
        }
    }
}