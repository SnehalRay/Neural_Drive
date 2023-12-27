using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public Transform target; // Reference to the car's transform
    public float distance = 10f; // Distance from the car
    public float height = 15f; // Height above the car
    public float rotationSpeed = 2f; // Adjust this to set the rotation speed of the camera
    public float smoothTime = 0.3f; // Smooth time for camera movement

    public float nearClipDistance = 0.001f; // Adjust this value to set the near clipping distance
    public float farClipDistance = 1000f; // Adjust this value to set the far clipping distance

    private float mouseX; // Mouse X position for rotation
    private float mouseY; // Mouse Y position for rotation
    private Vector3 velocity; // Velocity for SmoothDamp

    void Start()
    {
        // Set clipping planes in Start to avoid interference with other scripts
        GetComponent<Camera>().nearClipPlane = nearClipDistance;
        GetComponent<Camera>().farClipPlane = farClipDistance;
    }

    void LateUpdate()
    {
        if (target != null)
        {
            // Handle camera rotation with right mouse button
            if (Input.GetMouseButton(1))
            {
                mouseX += Input.GetAxis("Mouse X") * rotationSpeed;
                mouseY -= Input.GetAxis("Mouse Y") * rotationSpeed;
                mouseY = Mathf.Clamp(mouseY, -80f, 80f);

                // Set the rotation based on mouse input
                Quaternion rotation = Quaternion.Euler(mouseY, mouseX, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, rotation, Time.deltaTime * 10f);
            }
            else
            {
                // Always track the car's rotation when not actively adjusted
                Quaternion targetRotation = Quaternion.LookRotation(target.forward);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
            }

            // Update the camera position based on the car's position and offset
            Vector3 desiredPosition = target.position - target.forward * distance;
            desiredPosition.y = Mathf.Lerp(transform.position.y, Mathf.Clamp(target.position.y + height, 0f, Mathf.Infinity), Time.deltaTime * 10f);

            // Smoothly interpolate using SmoothDamp
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);
        }
    }
}
