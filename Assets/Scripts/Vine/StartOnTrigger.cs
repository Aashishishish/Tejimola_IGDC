using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attack zone for a vine: while the player is inside, the vine's attack bool is on.
/// The player has two colliders (CharacterController + CapsuleCollider), so it counts every player collider
/// inside and only turns the attack off when the last one leaves (otherwise one leaving early cancels the attack).
/// </summary>
public class StartOnTrigger : MonoBehaviour
{
    [Header("Animator & State Settings")]
    [SerializeField] private Animator vineAnimator;
    [SerializeField] private string attackBoolName = "isSlamming";
    [SerializeField] private string playerTag = "Player";

    private readonly HashSet<Collider> playerCollidersInside = new HashSet<Collider>();
    private int attackBoolHash;

    public Animator VineAnimator => vineAnimator;

    private void Awake()
    {
        attackBoolHash = Animator.StringToHash(attackBoolName);
        if (vineAnimator == null) Debug.LogError($"[StartOnTrigger] '{name}' has no Vine Animator assigned.", this);
    }

    // Puts the vine back to idle and stops listening (used when the vine is beaten)
    public void ForceStop()
    {
        playerCollidersInside.Clear();
        if (vineAnimator != null) vineAnimator.SetBool(attackBoolHash, false);
        enabled = false;
    }

    private bool IsPlayer(Collider other) =>
        other.CompareTag(playerTag) || other.transform.root.CompareTag(playerTag);

    private void OnTriggerEnter(Collider other)
    {
        // Unity still sends trigger messages to disabled scripts, so check it ourselves
        if (!enabled || vineAnimator == null || !IsPlayer(other)) return;

        playerCollidersInside.Add(other);
        vineAnimator.SetBool(attackBoolHash, true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!enabled || vineAnimator == null) return;
        if (!playerCollidersInside.Remove(other)) return;

        if (playerCollidersInside.Count == 0) vineAnimator.SetBool(attackBoolHash, false);
    }
}
