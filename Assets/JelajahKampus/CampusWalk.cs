using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class CampusWalk : MonoBehaviour
{
    [SerializeField] private Transform view;
    [SerializeField, Min(0f)] private float walkSpeed = 2f;
    [SerializeField, Min(0.5f)] private float desktopEyeHeight = 1.65f;
    [SerializeField, Range(15f, 90f)] private float snapAngle = 30f;
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.12f;

    private readonly List<XRInputSubsystem> inputSubsystems = new();
    private CharacterController body;
    private TrackedPoseDriver poseDriver;
    private XRInputSubsystem xrInput;
    private InputAction xrMove;
    private InputAction xrTurn;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float fallSpeed;
    private float pitch;
    private bool usingHeadset;
    private bool turnReady = true;
    private bool lookingWithMouse;

    private void Awake()
    {
        body = GetComponent<CharacterController>();
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        if (view == null || view.parent == null || view.parent.parent != transform)
        {
            Debug.LogError("Campus Walk memerlukan kamera di dalam objek Camera Offset.", this);
            enabled = false;
            return;
        }

        poseDriver = view.GetComponent<TrackedPoseDriver>();
        InputActionMap xrActions = InputSystem.actions?.FindActionMap("Jelajah XR");
        if (xrActions != null && poseDriver != null)
        {
            xrMove = xrActions.FindAction("Move", throwIfNotFound: true);
            xrTurn = xrActions.FindAction("Turn", throwIfNotFound: true);
            poseDriver.positionInput = new InputActionProperty(xrActions.FindAction("Head Position", true));
            poseDriver.rotationInput = new InputActionProperty(xrActions.FindAction("Head Rotation", true));
            poseDriver.trackingStateInput = new InputActionProperty(xrActions.FindAction("Head Tracking", true));
        }
    }

    private void OnDisable() => ReleaseMouse();

    private void LateUpdate()
    {
        bool headsetActive = poseDriver != null && xrMove != null && XRSettings.isDeviceActive;
        if (headsetActive != usingHeadset)
        {
            usingHeadset = headsetActive;
            poseDriver.enabled = headsetActive;
            xrInput = null;
            turnReady = true;
            ReleaseMouse();

            if (!headsetActive)
            {
                view.parent.localPosition = Vector3.up * desktopEyeHeight;
                view.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(pitch, 0f, 0f));
            }
        }

        Vector2 input;
        if (usingHeadset)
        {
            UpdateTrackingOrigin();
            TurnWithThumbstick(xrTurn.ReadValue<Vector2>().x);
            input = xrMove.ReadValue<Vector2>();
        }
        else
        {
            input = ReadDesktopInput();
        }

        Vector3 headPosition = transform.InverseTransformPoint(view.position);
        float height = Mathf.Clamp(headPosition.y, 2f * body.radius, 2.35f);
        if (Mathf.Abs(body.height - height) > 0.001f)
            body.height = height;
        Vector3 center = new Vector3(headPosition.x, body.height * 0.5f, headPosition.z);
        if ((body.center - center).sqrMagnitude > 0.000001f)
            body.center = center;

        Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        input = Vector2.ClampMagnitude(input, 1f);

        float deltaTime = Time.deltaTime;
        fallSpeed = body.isGrounded ? -2f : Mathf.Max(fallSpeed + Physics.gravity.y * deltaTime, -25f);
        Vector3 velocity = (forward * input.y + right * input.x) * walkSpeed;
        velocity.y = fallSpeed;
        body.Move(velocity * deltaTime);

        if (transform.position.y < spawnPosition.y - 15f)
        {
            body.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            body.enabled = true;
            fallSpeed = 0f;
        }
    }

    private void UpdateTrackingOrigin()
    {
        if (xrInput == null || !xrInput.running)
        {
            SubsystemManager.GetSubsystems(inputSubsystems);
            xrInput = inputSubsystems.Find(subsystem => subsystem.running);
            if (xrInput != null)
                xrInput.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
        }

        bool floorTracking = xrInput != null &&
            (xrInput.GetTrackingOriginMode() & TrackingOriginModeFlags.Floor) != 0;
        view.parent.localPosition = floorTracking ? Vector3.zero : Vector3.up * desktopEyeHeight;
    }

    private void TurnWithThumbstick(float turn)
    {
        if (Mathf.Abs(turn) < 0.25f)
            turnReady = true;

        if (turnReady && Mathf.Abs(turn) > 0.75f)
        {
            transform.RotateAround(view.position, Vector3.up, Mathf.Sign(turn) * snapAngle);
            turnReady = false;
        }
    }

    private Vector2 ReadDesktopInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed && Application.isFocused)
        {
            if (lookingWithMouse)
            {
                Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
                transform.Rotate(0f, look.x, 0f, Space.World);
                pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
                view.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
            else
            {
                lookingWithMouse = true;
                Cursor.lockState = CursorLockMode.Locked;
            }
        }
        else
        {
            ReleaseMouse();
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return Vector2.zero;

        return new Vector2(
            (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
            (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
    }

    private void ReleaseMouse()
    {
        if (!lookingWithMouse)
            return;

        lookingWithMouse = false;
        Cursor.lockState = CursorLockMode.None;
    }
}
