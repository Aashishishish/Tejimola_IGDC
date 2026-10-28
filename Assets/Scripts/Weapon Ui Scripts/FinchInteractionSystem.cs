using System.Collections.Generic;
using UnityEngine;

public class FinchInteractionSystem : MonoBehaviour
{
    [Header("Distance Settings")]
    [SerializeField] private float maxDetectionDistance = 8f; 
    [SerializeField] private float closeInteractionDistance = 3f; 

    [Header("Raycast Settings")]
    [SerializeField] private LayerMask interactableLayer;

    [Header("UI References")]
    [SerializeField] private GameObject reticleDotObject;
    [SerializeField] private GameObject handPromptObject;

    [Header("World Markers (dot / hand shown on top of every interactable object)")]
    [Tooltip("ON: each interactable (weapon, axe, lantern...) gets its own dot / hand on top of it. OFF: the old single dot / hand at the screen centre.")]
    [SerializeField] private bool showMarkersOnObjects = true;
    [Tooltip("Metres above the top of the object where the dot / hand is drawn")]
    [SerializeField] private float markerHeightAbove = 0.15f;
    [Tooltip("What can hide an object from view (walls, terrain...). The marker is hidden while something blocks the line of sight.")]
    [SerializeField] private LayerMask occluderLayers = ~0;
    [Tooltip("To pick something up (E) you must be within the close distance AND looking roughly towards it (degrees)")]
    [SerializeField] private float pickupViewAngle = 50f;

    [Tooltip("Size of each dot / hand relative to the template (1 = same size)")]
    [SerializeField] private float markerScale = 0.45f;

    private class Marker { public GameObject dot; public GameObject hand; }
    private readonly Dictionary<Transform, Marker> markers = new Dictionary<Transform, Marker>();
    private readonly HashSet<Transform> seenThisFrame = new HashSet<Transform>();
    private Camera viewCamera;
    private readonly HashSet<Transform> dropped = new HashSet<Transform>();

    /// <summary>Called when the player drops a weapon / prop: from now on its dot / hand hovers over it.</summary>
    public void MarkDropped(Transform t) { if (t != null) dropped.Add(t); }

    /// <summary>Called when the player picks something up.</summary>
    public void MarkPickedUp(Transform t) { if (t != null) dropped.Remove(t); }

    private Transform currentTarget;
    private float currentDistance;
    private InspectAndCutSystem inspectSystem;

    private void Awake()
    {
        inspectSystem = GetComponent<InspectAndCutSystem>();
    }

    private void Start()
    {
        HideAllUI();
    }

    private void Update()
    {
        // If already holding an object, hide targeting UI and don't look for new props
        if (inspectSystem != null && inspectSystem.IsHoldingObject())
        {
            HideAllUI();
            HideAllMarkers();
            return;
        }

        // Objects lying on the ground (dropped) get a dot / hand hovering over them.
        Transform markerTarget = null;
        float markerDistance = float.MaxValue;
        if (showMarkersOnObjects && reticleDotObject != null && handPromptObject != null)
        {
            UpdateMarkers();
            markerTarget = currentTarget;
            markerDistance = currentDistance;
        }

        // Everything else: dot / hand in the centre of the screen while you look at it
        PerformRaycast();
        if (currentTarget == null && markerTarget != null)
        {
            currentTarget = markerTarget;
            currentDistance = markerDistance;
        }
        HandleInteractionInput();
    }

    // ------------------------------------------------------------------ world markers

    private void UpdateMarkers()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) return;

        seenThisFrame.Clear();
        currentTarget = null;
        currentDistance = float.MaxValue;
        float bestAngle = pickupViewAngle;
        int occlusionMask = occluderLayers & ~(1 << transform.root.gameObject.layer);   // never blocked by the player

        Collider[] found = Physics.OverlapSphere(transform.position, maxDetectionDistance, interactableLayer, QueryTriggerInteraction.Collide);
        foreach (Collider col in found)
        {
            Transform target = col.attachedRigidbody != null ? col.attachedRigidbody.transform : col.transform;
            if (!dropped.Contains(target)) continue;      // only objects that were dropped hover their own marker
            if (!seenThisFrame.Add(target)) continue;   // another collider of the same object

            // size of the whole object
            Bounds b = col.bounds;
            foreach (Collider c in target.GetComponentsInChildren<Collider>())
                b.Encapsulate(c.bounds);

            float distance = Vector3.Distance(transform.position, b.ClosestPoint(transform.position));
            Vector3 screen = viewCamera.WorldToScreenPoint(new Vector3(b.center.x, b.max.y + markerHeightAbove, b.center.z));
            bool onScreen = screen.z > 0f && screen.x > 0f && screen.x < Screen.width && screen.y > 0f && screen.y < Screen.height;
            Vector3 topPoint = new Vector3(b.center.x, b.max.y + markerHeightAbove, b.center.z);
            bool belowGround = false;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                float groundY = terrain.SampleHeight(b.center) + terrain.transform.position.y;
                belowGround = b.center.y < groundY - 0.5f;   // fell through / outside the terrain
            }
            // hidden when walls block the view of both the object and the spot above it
            bool visible = onScreen && !belowGround && !(IsHidden(b.center, target, occlusionMask) && IsHidden(topPoint, target, occlusionMask));
            bool near = distance <= closeInteractionDistance;

            Marker m = GetMarker(target);
            bool showDot = visible && !near;     // far: dot
            bool showHand = visible && near;     // close: hand, until it is picked up
            if (m.dot.activeSelf != showDot) m.dot.SetActive(showDot);
            if (m.hand.activeSelf != showHand) m.hand.SetActive(showHand);
            Vector3 uiPos = new Vector3(screen.x, screen.y, 0f);   // Screen Space Overlay canvas: position = screen pixel
            if (showDot) m.dot.transform.position = uiPos;
            if (showHand) m.hand.transform.position = uiPos;

            // the closest-to-crosshair object within reach is the one E picks up
            if (showHand)
            {
                float angle = Vector3.Angle(transform.forward, b.center - transform.position);
                if (angle < bestAngle) { bestAngle = angle; currentTarget = target; currentDistance = distance; }
            }
        }

        // objects that are out of range / gone: hide (or clean up) their markers
        foreach (var kv in markers)
        {
            if (kv.Key == null)
            {
                if (kv.Value.dot != null) Destroy(kv.Value.dot);
                if (kv.Value.hand != null) Destroy(kv.Value.hand);
            }
            else if (!seenThisFrame.Contains(kv.Key))
            {
                if (kv.Value.dot.activeSelf) kv.Value.dot.SetActive(false);
                if (kv.Value.hand.activeSelf) kv.Value.hand.SetActive(false);
            }
        }
    }

    // True if something other than the object itself is between the camera and the object
    private bool IsHidden(Vector3 point, Transform target, int mask)
    {
        if (!Physics.Linecast(transform.position, point, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore)) return false;
        Transform t = hit.collider.attachedRigidbody != null ? hit.collider.attachedRigidbody.transform : hit.collider.transform;
        return t != target && !hit.collider.transform.IsChildOf(target);
    }

    private Marker GetMarker(Transform target)
    {
        if (markers.TryGetValue(target, out Marker m) && m.dot != null && m.hand != null) return m;

        m = new Marker
        {
            dot = Instantiate(reticleDotObject, reticleDotObject.transform.parent),
            hand = Instantiate(handPromptObject, handPromptObject.transform.parent)
        };
        m.dot.name = "Dot on " + target.name;
        m.hand.name = "Hand on " + target.name;
        m.dot.transform.localScale = reticleDotObject.transform.localScale * markerScale;
        m.hand.transform.localScale = handPromptObject.transform.localScale * markerScale;
        m.dot.SetActive(false);
        m.hand.SetActive(false);
        markers[target] = m;
        return m;
    }

    private void HideAllMarkers()
    {
        foreach (var kv in markers)
        {
            if (kv.Value.dot != null && kv.Value.dot.activeSelf) kv.Value.dot.SetActive(false);
            if (kv.Value.hand != null && kv.Value.hand.activeSelf) kv.Value.hand.SetActive(false);
        }
    }

    private void PerformRaycast()
    {
        Ray ray = new Ray(transform.position, transform.forward);
        int playerLayer = transform.root.gameObject.layer;
        int mask = (interactableLayer | occluderLayers) & ~(1 << playerLayer);

        // First thing along the view direction: an interactable (not yet dropped) or a wall that hides it
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDetectionDistance, mask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));

        Transform found = null;
        float foundDistance = 0f;
        foreach (RaycastHit hit in hits)
        {
            Transform t = hit.collider.attachedRigidbody != null ? hit.collider.attachedRigidbody.transform : hit.collider.transform;
            bool isInteractable = (interactableLayer.value & (1 << hit.collider.gameObject.layer)) != 0;
            if (isInteractable)
            {
                if (dropped.Contains(t)) continue;     // dropped objects use their hovering marker instead
                found = t;
                foundDistance = hit.distance;
            }
            break;   // either the interactable, or something solid in front of it
        }

        if (found != null)
        {
            currentTarget = found;
            currentDistance = foundDistance;

            // ZONE 1: Close Range (Hand Animation ONLY)
            if (foundDistance <= closeInteractionDistance)
            {
                if (reticleDotObject != null && reticleDotObject.activeSelf)
                    reticleDotObject.SetActive(false);

                if (handPromptObject != null && !handPromptObject.activeSelf)
                    handPromptObject.SetActive(true);
            }
            // ZONE 2: Far Range (Reticle Dot ONLY)
            else
            {
                ShowReticleOnly();
            }
        }
        else
        {
            // Looking away from every interactable: no dot, no hand in the centre
            currentTarget = null;
            HideAllUI();
        }
    }

    private void ShowReticleOnly()
    {
        if (reticleDotObject != null && !reticleDotObject.activeSelf)
            reticleDotObject.SetActive(true);

        if (handPromptObject != null && handPromptObject.activeSelf)
            handPromptObject.SetActive(false);
    }

    private void HandleInteractionInput()
    {
        if (currentTarget != null && currentDistance <= closeInteractionDistance)
        {
            // E picks the weapon up (E also drops it again, handled in InspectAndCutSystem). X is reserved for swinging.
            if (inspectSystem != null && Input.GetKeyDown(inspectSystem.PickupDropKey))
            {
                // Pass target to held system
                if (inspectSystem != null)
                {
                    MarkPickedUp(currentTarget);
                    inspectSystem.EquipObject(currentTarget);
                    HideAllUI();
                    HideAllMarkers();
                }
            }
        }
    }

    private void HideAllUI()
    {
        if (reticleDotObject != null && reticleDotObject.activeSelf)
            reticleDotObject.SetActive(false);

        if (handPromptObject != null && handPromptObject.activeSelf)
            handPromptObject.SetActive(false);
    }
}