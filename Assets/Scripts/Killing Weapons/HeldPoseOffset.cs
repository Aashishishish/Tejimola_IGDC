using UnityEngine;

/// <summary>
/// Optional, on a weapon / prop. Changes how it sits in the hand when picked up.
/// Extra Euler: extra rotation (in the object's own axes) on top of the shared hold pose, e.g. for a model that faces the wrong way.
/// Keep Upright: the object always hangs straight down (e.g. a lantern), ignoring the tilt of the hand / swing.
/// </summary>
public class HeldPoseOffset : MonoBehaviour
{
    public Vector3 extraEuler = new Vector3(0f, 180f, 0f);
    [Tooltip("Hang straight (world up), only turning with the camera's left/right direction")]
    public bool keepUpright;
    [Tooltip("Turn (degrees) around the vertical axis relative to the camera while held")]
    public float uprightYawOffset = 0f;

    [Tooltip("Size while held, relative to its normal size (1 = same)")]
    public float heldScale = 1f;

    private Rigidbody body;
    private Vector3 normalScale;
    private bool scaled;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void LateUpdate()
    {
        // held = parented to the hand anchor and frozen
        bool held = transform.parent != null && (body == null || body.isKinematic);

        if (!Mathf.Approximately(heldScale, 1f))
        {
            if (held && !scaled) { normalScale = transform.localScale; transform.localScale = normalScale * heldScale; scaled = true; }
            else if (!held && scaled) { transform.localScale = normalScale; scaled = false; }
        }

        if (!keepUpright || !held) return;

        float yaw = transform.parent.eulerAngles.y + uprightYawOffset;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }
}
