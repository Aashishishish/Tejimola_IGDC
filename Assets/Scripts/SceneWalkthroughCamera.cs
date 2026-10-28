using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SceneWalkthroughCamera : MonoBehaviour
{
    [Header("Movement Speeds")]
    [SerializeField] private float normalSpeed = 8f;
    [SerializeField] private float sprintMultiplier = 2.5f;
    [SerializeField] private float slowMultiplier = 0.3f;
    [SerializeField] private float minSpeed = 1f;
    [SerializeField] private float maxSpeed = 40f;
    [SerializeField] private float scrollSpeedSensitivity = 5f;

    [Header("Look Sensitivity & Limits")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;
    [SerializeField] private bool invertY = false;

    private float currentYaw;
    private float currentPitch;

    private void Start()
    {
        // Align starting rotations with the camera's initial transform
        Vector3 initialRotation = transform.eulerAngles;
        currentYaw = initialRotation.y;
        currentPitch = initialRotation.x;

        // Convert pitch if rotation starts wrapped past 180 degrees
        if (currentPitch > 180f) currentPitch -= 360f;

        SetCursorLock(true);
    }

    private void Update()
    {
        HandleCursorLockToggle();

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            HandleRotation();
            HandleMovement();
            HandleSpeedScroll();
        }
    }

    private void HandleRotation()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);

        currentYaw += mouseX;
        currentPitch = Mathf.Clamp(currentPitch - mouseY, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
    }

    private void HandleMovement()
    {
        // Adjust speed modifiers
        float activeSpeed = normalSpeed;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            activeSpeed *= sprintMultiplier;
        }
        else if (Input.GetKey(KeyCode.LeftControl))
        {
            activeSpeed *= slowMultiplier;
        }

        // Translation vector relative to orientation
        Vector3 moveDirection = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) moveDirection += transform.forward;
        if (Input.GetKey(KeyCode.S)) moveDirection -= transform.forward;
        if (Input.GetKey(KeyCode.D)) moveDirection += transform.right;
        if (Input.GetKey(KeyCode.A)) moveDirection -= transform.right;
        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) moveDirection += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) moveDirection -= Vector3.up;

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            transform.position += moveDirection.normalized * (activeSpeed * Time.deltaTime);
        }
    }

    private void HandleSpeedScroll()
    {
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            normalSpeed = Mathf.Clamp(normalSpeed + (scrollInput * scrollSpeedSensitivity), minSpeed, maxSpeed);
        }
    }

    private void HandleCursorLockToggle()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorLock(false);
        }
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLock(true);
        }
    }

    private void SetCursorLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}