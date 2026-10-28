using System.Collections;
using UnityEngine;

/// <summary>
/// One per vine, on the vine root (the object with the kinematic Rigidbody).
/// Unity sends trigger messages from every child collider of a Rigidbody to the Rigidbody's object too,
/// so the per-joint trigger capsules underneath all report here; no script is needed on each joint.
/// Hurts the player on contact and knocks them back once per counted hit.
/// </summary>
public class VineDamageHitbox : MonoBehaviour
{
    [Tooltip("Only colliders on these layers are checked (cheap filter: the vine also overlaps terrain, the shed and the other vine)")]
    [SerializeField] private LayerMask playerLayers = 1 << 8;   // "Player"

    [Header("Knockback (only when the hit counts)")]
    [Tooltip("Metres the player is shoved away from the vine")]
    [SerializeField] private float knockbackDistance = 1.2f;
    [SerializeField] private float knockbackSeconds = 0.18f;

    [Header("Sound")]
    [Tooltip("Optional: played when the vine hits the player (a whip / thwack)")]
    [SerializeField] private AudioClip hitPlayerSound;
    [Range(0f, 1f)] [SerializeField] private float hitPlayerVolume = 1f;

    [Header("VFX")]
    [Tooltip("Spawned on the player when the vine hits them (e.g. BloodFlakes_Hit)")]
    [SerializeField] private GameObject hitPlayerVfx;
    [Tooltip("Height above the player's feet to spawn it (chest)")]
    [SerializeField] private float vfxHeight = 1.2f;

    // Cached on first contact; the player never changes
    private PlayerHitDeath playerHealth;
    private CharacterController playerController;
    private Coroutine knockbackRoutine;

    private void OnTriggerEnter(Collider other) => HandleContact(other);

    // Covers the vine slamming down onto a player who is standing still; TakeHit's invulnerability window limits repeats.
    private void OnTriggerStay(Collider other) => HandleContact(other);

    private void HandleContact(Collider other)
    {
        // Unity still sends trigger messages to disabled scripts; VineHealth disables this to make the vine harmless
        if (!enabled) return;
        if ((playerLayers.value & (1 << other.gameObject.layer)) == 0) return;

        if (playerHealth == null)
        {
            playerHealth = other.GetComponentInParent<PlayerHitDeath>();
            if (playerHealth == null) return;
            playerController = playerHealth.GetComponentInParent<CharacterController>();
            if (playerController == null) playerController = playerHealth.GetComponentInChildren<CharacterController>();
        }

        if (!playerHealth.TakeHit()) return;   // invulnerable or dead: no knockback either

        if (hitPlayerSound != null) AudioSource.PlayClipAtPoint(hitPlayerSound, other.transform.position, hitPlayerVolume);
        VfxSpawner.Spawn(hitPlayerVfx, playerHealth.transform.position + Vector3.up * vfxHeight);

        // Shove away from the vine's base, flat on the ground (the message doesn't say which joint touched)
        Vector3 away = other.transform.position - transform.position;
        away.y = 0f;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : -other.transform.forward;

        if (knockbackRoutine != null) StopCoroutine(knockbackRoutine);
        knockbackRoutine = StartCoroutine(Knockback(away));
    }

    // Spread over a few frames so it reads as a shove, not a teleport; CharacterController.Move respects walls
    private IEnumerator Knockback(Vector3 dir)
    {
        float speed = knockbackDistance / Mathf.Max(0.01f, knockbackSeconds);
        for (float t = 0f; t < knockbackSeconds; t += Time.deltaTime)
        {
            if (playerController != null && playerController.enabled)
                playerController.Move(dir * speed * Time.deltaTime);
            yield return null;
        }
        knockbackRoutine = null;
    }
}
