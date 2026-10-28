using UnityEngine;

/// <summary>
/// An invisible zone (trigger collider). When the player walks into it, the task with this id is completed.
/// Move / resize the collider in the Scene view to place it where the task should be done.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TaskTriggerZone : MonoBehaviour
{
    [Tooltip("Id of the task in GameTasks that this zone completes")]
    [SerializeField] private string taskId = "backyard";

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        bool isPlayer = other.CompareTag("Player") || (other.transform.root != null && other.transform.root.CompareTag("Player"));
        if (!isPlayer) return;

        if (GameTasks.Instance != null) GameTasks.Instance.Complete(taskId);
    }
}
