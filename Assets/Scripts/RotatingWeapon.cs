using UnityEngine;

public class RotatingWeapon : MonoBehaviour
{
    [Header("Rotation Settings")]
    public float rotationSpeed = 50f;

    [Header("Hover Settings")]
    public float hoverAmplitude = 0.015f; // Movement height
    public float hoverFrequency = 2f; // UP/DOWN movement speed

    private Vector3 initialLocalPosition;

    void Start()
    {
        initialLocalPosition = transform.localPosition;
    }

    void Update()
    {
        // 1\. Rotates the sword on its Y axis
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

        // 2\. Makes the sword float smoothly up and down
        float newY = initialLocalPosition.y + Mathf.Sin(Time.time * hoverFrequency) * hoverAmplitude;
        transform.localPosition = new Vector3(initialLocalPosition.x, newY, initialLocalPosition.z);
    }
}