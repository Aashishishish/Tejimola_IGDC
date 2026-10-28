using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float sprintSpeed = 10f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float jumpHeight = 1.5f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.3f;
    [SerializeField] private LayerMask groundMask;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator animator;

    [Header("Animation")]
    [SerializeField] private string speedParameter = "Speed";
    [Tooltip("Smoothing time (seconds) Animator.SetFloat uses internally to ease Speed changes. Higher = smoother/slower to respond, lower = snappier.")]
    [SerializeField] private float speedDampTime = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool debugLogging = false;

    private Rigidbody rb;
    private Vector2 moveInput;
    private bool sprintHeld;
    private bool jumpQueued;
    private bool isGrounded;

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Force correct Rigidbody settings in code so a stray Inspector checkbox
        // can never silently break physics again.
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.freezeRotation = true; // we rotate the player manually; don't let physics topple the capsule
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Discrete is the right choice for a walking character. ContinuousDynamic
        // is meant for small, fast-moving objects (e.g. bullets) and is extremely
        // expensive — and can hang/crash the editor — when checked against a
        // Terrain Collider's large non-convex heightmap mesh.
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // Movement (WASD / left stick)
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddBinding("<Gamepad>/leftStick");

        // Jump
        jumpAction = new InputAction("Jump", binding: "<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        // Sprint
        sprintAction = new InputAction("Sprint", binding: "<Keyboard>/leftShift");
        sprintAction.AddBinding("<Gamepad>/leftStickPress");
    }

    private void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
        jumpAction.performed += OnJump;
    }

    private void OnDisable()
    {
        jumpAction.performed -= OnJump;
        moveAction.Disable();
        jumpAction.Disable();
        sprintAction.Disable();
    }

    private void Update()
    {
        // Read input every frame for responsiveness. It's only *applied* to
        // the Rigidbody in FixedUpdate, which is where physics should happen.
        moveInput = moveAction.ReadValue<Vector2>();
        sprintHeld = sprintAction.IsPressed();

        UpdateAnimator();

        if (debugLogging && moveInput.sqrMagnitude > 0.0001f)
            Debug.Log($"[Input] moveInput={moveInput}");
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        // Horizontal speed only — ignore vertical (falling/jumping) velocity
        // so landing or falling doesn't spike the Speed parameter.
        Vector3 horizontalVelocity = rb.linearVelocity;
        horizontalVelocity.y = 0f;
        float currentSpeed = horizontalVelocity.magnitude;

        // Normalize against sprintSpeed so 0 = idle, ~0.6 = walk, 1 = full sprint,
        // which maps cleanly onto a Blend Tree using this same Speed parameter.
        float normalizedSpeed = sprintSpeed > 0f ? currentSpeed / sprintSpeed : 0f;

        animator.SetFloat(speedParameter, normalizedSpeed, speedDampTime, Time.deltaTime);
    }

    private void FixedUpdate()
    {
        GroundCheck();
        Move();
        HandleJump();

        if (debugLogging)
            Debug.Log($"[FixedUpdate] pos={transform.position} vel={rb.linearVelocity} grounded={isGrounded}");
    }

    private void GroundCheck()
    {
        if (groundCheck != null)
        {
            isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundMask);
        }
        else
        {
            // Fallback if no groundCheck transform is assigned: short ray from the player's center.
            isGrounded = Physics.Raycast(transform.position, Vector3.down, 1.1f, groundMask);
        }
    }

    private void Move()
    {
        Vector3 inputDir = new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 horizontalVelocity = Vector3.zero;

        if (inputDir.sqrMagnitude > 0.0001f && cameraTransform != null)
        {
            // Make input relative to where the camera is looking
            Vector3 camForward = cameraTransform.forward;
            Vector3 camRight = cameraTransform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();

            Vector3 moveDir = (camForward * inputDir.z + camRight * inputDir.x).normalized;
            float speed = sprintHeld ? sprintSpeed : moveSpeed;
            horizontalVelocity = moveDir * speed;

            // Smoothly rotate the player to face the direction they're moving
            Quaternion targetRotation = Quaternion.LookRotation(moveDir, Vector3.up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }

        // Only overwrite horizontal velocity — leave vertical velocity alone so
        // gravity (and jumps) applied by the physics engine keep working normally.
        Vector3 currentVelocity = rb.linearVelocity;
        rb.linearVelocity = new Vector3(horizontalVelocity.x, currentVelocity.y, horizontalVelocity.z);
    }

    private void HandleJump()
    {
        if (jumpQueued && isGrounded)
        {
            float jumpVelocity = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpVelocity, rb.linearVelocity.z);
        }
        jumpQueued = false;
    }

    private void OnJump(InputAction.CallbackContext context)
    {
        // Queue the jump; it's actually applied in FixedUpdate so it lines up
        // with the same physics step as everything else.
        jumpQueued = true;
    }
}