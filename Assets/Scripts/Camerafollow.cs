using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Distance")]
    [SerializeField] private float distance = 5f;
    [SerializeField] private float minDistance = 1.5f;
    [SerializeField] private float maxDistance = 8f;
    [SerializeField] private float zoomSpeed = 2f;

    [Header("Rotation")]
    [SerializeField] private float mouseSensitivity = 3f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 70f;
    [Tooltip("Added to the target's own facing direction to set the camera's starting horizontal angle. 0 = directly behind the player; 180 = directly in front.")]
    [SerializeField] private float startYawOffset = 0f;
    [Tooltip("The camera's starting up/down angle.")]
    [SerializeField] private float startPitch = 20f;

    [Header("Collision")]
    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private float collisionBuffer = 0.1f;
    [Tooltip("Frames to wait after enabling before the collision SphereCast starts running. Avoids querying physics before a Terrain Collider has finished cooking, which can hang/crash the editor.")]
    [SerializeField] private int collisionCheckStartupDelayFrames = 5;

    private float yaw;
    private float pitch;
    private float currentDistance;
    private Vector2 lookInput;
    private int framesSinceEnabled;

    private InputAction lookAction;
    private InputAction zoomAction;

    private void Awake()
    {
        currentDistance = distance;

        // Mouse look
        lookAction = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
        lookAction.AddBinding("<Mouse>/delta");

        // Scroll to zoom
        zoomAction = new InputAction("Zoom", InputActionType.Value, expectedControlType: "Axis");
        zoomAction.AddBinding("<Mouse>/scroll/y");
    }

    private void OnEnable()
    {
        lookAction.Enable();
        zoomAction.Enable();
        framesSinceEnabled = 0;
    }

    private void OnDisable()
    {
        lookAction.Disable();
        zoomAction.Disable();
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        pitch = Mathf.Clamp(startPitch, minPitch, maxPitch);

        if (target != null)
            yaw = target.eulerAngles.y + startYawOffset;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        framesSinceEnabled++;

        HandleRotationInput();
        HandleZoomInput();
        UpdateCameraPosition();
    }

    private void HandleRotationInput()
    {
        lookInput = lookAction.ReadValue<Vector2>();

        yaw += lookInput.x * mouseSensitivity * Time.deltaTime * 60f;
        pitch -= lookInput.y * mouseSensitivity * Time.deltaTime * 60f;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void HandleZoomInput()
    {
        float scroll = zoomAction.ReadValue<float>();
        distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);
    }

    private void UpdateCameraPosition()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + targetOffset;
        Vector3 desiredPosition = pivot - (rotation * Vector3.forward * distance);

        // Push the camera in if it would clip through a wall.
        // Skip this during the first few frames after enabling — Terrain Colliders
        // can still be cooking their collision mesh at that point, and querying
        // physics against an unfinished collider can hang or crash the editor.
        float targetDistance = distance;
        if (framesSinceEnabled > collisionCheckStartupDelayFrames &&
            Physics.SphereCast(pivot, collisionRadius, (desiredPosition - pivot).normalized,
                out RaycastHit hit, distance, collisionMask))
        {
            targetDistance = Mathf.Clamp(hit.distance - collisionBuffer, minDistance, distance);
        }

        currentDistance = Mathf.Lerp(currentDistance, targetDistance, Time.deltaTime * 10f);
        Vector3 finalPosition = pivot - (rotation * Vector3.forward * currentDistance);

        transform.position = finalPosition;
        transform.rotation = rotation;
    }
}