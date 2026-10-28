using System.Collections;
using UnityEngine;

/// <summary>
/// Makes a vine collapse with real physics: stops its animation (the pose freezes), turns its
/// "fall colliders" solid, and hands the root to a dynamic Rigidbody with gravity so it topples
/// the way it was already leaning and lands on the terrain. Once it has settled it is frozen
/// again (kinematic) so it costs nothing.
///
/// Used by VineHealth when a guard vine is beaten, and by BottleGourdEncounter to drop the NPC vines
/// when the gourd falls. Put it on the vine root (the object with the Animator).
///
/// Fall colliders: capsules baked per bone in the editor (mesh vertices can't be read at runtime in a
/// build unless Read/Write is on, so they are generated ahead of time and left disabled or as triggers).
/// </summary>
[DisallowMultipleComponent]
public class VineCollapse : MonoBehaviour
{
    [Header("Links (auto-found if empty)")]
    [SerializeField] private Animator vineAnimator;
    [Tooltip("Colliders that become solid for the fall (disabled or trigger until then). Empty = every CapsuleCollider under this vine.")]
    [SerializeField] private Collider[] fallColliders;

    [Header("Physics")]
    [SerializeField] private float mass = 30f;
    [SerializeField] private float linearDamping = 0.2f;
    [SerializeField] private float angularDamping = 1.5f;
    [Tooltip("Spin (impulse) that starts the topple, so a vine standing straight up still falls")]
    [SerializeField] private float tipTorque = 25f;
    [Tooltip("Lift before the fall so the base doesn't start inside the terrain (metres)")]
    [SerializeField] private float liftOnCollapse = 0.2f;
    [Tooltip("Caps how hard physics pushes it out of the ground, so overlaps don't launch it")]
    [SerializeField] private float maxDepenetrationVelocity = 2f;
    [Tooltip("Fallen vines don't block the player (they still land on the ground)")]
    [SerializeField] private LayerMask ignoreLayersWhenFallen = 1 << 8;   // "Player"
    [Tooltip("Colliders the vine falls straight through (e.g. the shed it is draped over), so it ends up on the ground")]
    [SerializeField] private Collider[] fallThrough;

    [Header("Settle")]
    [Tooltip("Freeze the vine again after this long even if it is still moving")]
    [SerializeField] private float settleTimeout = 6f;
    [Tooltip("Safety net: freeze if it has dropped this far (fell through the ground)")]
    [SerializeField] private float maxDrop = 12f;

    public bool HasCollapsed { get; private set; }

    private Rigidbody body;

    private void Awake()
    {
        if (vineAnimator == null) vineAnimator = GetComponent<Animator>();
        if (fallColliders == null || fallColliders.Length == 0) fallColliders = GetComponentsInChildren<CapsuleCollider>(true);
    }

    /// <summary>Collapse now. pushDirection (optional) is the horizontal direction to topple toward.</summary>
    public void Collapse(Vector3 pushDirection = default)
    {
        if (HasCollapsed) return;
        HasCollapsed = true;

        if (vineAnimator != null) vineAnimator.enabled = false;   // freezes the current pose

        foreach (var c in fallColliders)
        {
            if (c == null) continue;
            c.isTrigger = false;
            c.excludeLayers = ignoreLayersWhenFallen;
            c.enabled = true;
            if (fallThrough != null)
                foreach (var other in fallThrough) if (other != null) Physics.IgnoreCollision(c, other, true);
        }

        body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.mass = mass;
        body.linearDamping = linearDamping;
        body.angularDamping = angularDamping;
        body.maxDepenetrationVelocity = maxDepenetrationVelocity;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.interpolation = RigidbodyInterpolation.Interpolate;   // smooth on screen while it falls
        body.excludeLayers = ignoreLayersWhenFallen;

        transform.position += Vector3.up * liftOnCollapse;
        Physics.SyncTransforms();

        body.isKinematic = false;
        body.useGravity = true;

        // Tip it: spin around the horizontal axis that tilts its top toward pushDirection
        Vector3 dir = new Vector3(pushDirection.x, 0f, pushDirection.z);
        if (dir.sqrMagnitude < 0.0001f)
        {
            Vector2 r = Random.insideUnitCircle;   // no direction given (NPC vines): any way it likes
            dir = r.sqrMagnitude > 0.0001f ? new Vector3(r.x, 0f, r.y) : Vector3.forward;
        }
        Vector3 axis = Vector3.Cross(Vector3.up, dir.normalized);
        body.AddTorque(axis * tipTorque, ForceMode.Impulse);

        StartCoroutine(SettleRoutine());
    }

    private IEnumerator SettleRoutine()
    {
        float startY = transform.position.y;
        float started = Time.time;
        yield return new WaitForSeconds(0.5f);   // let it get going before checking for sleep

        while (Time.time - started < settleTimeout && !body.IsSleeping() && startY - transform.position.y < maxDrop)
            yield return null;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
    }
}
