using UnityEngine;
using UnityEngine.Splines; // Required for Spline functionality

public class SplineObjectArray : MonoBehaviour
{
    [Header("Spline Settings")]
    public SplineContainer splineContainer; // Drag your Spline GameObject here
    public int objectCount = 10;            // How many objects to put in the array

    [Header("Prefab Settings")]
    public GameObject prefabToSpawn;        // The object you want to duplicate

    [Header("Result Array")]
    public GameObject[] spawnedObjects;     // The final array storing your GameObjects

    void Start()
    {
        if (splineContainer == null || prefabToSpawn == null)
        {
            Debug.LogError("Please assign the Spline Container and Prefab in the Inspector.");
            return;
        }

        // Initialize the array size
        spawnedObjects = new GameObject[objectCount];

        // Loop to calculate points along the spline and spawn objects
        for (int i = 0; i < objectCount; i++)
        {
            // Calculate a normalized position 't' from 0.0 (start) to 1.0 (end)
            float t = (float)i / (objectCount - 1);

            // Get the 3D position and rotation at that specific point on the spline
            Vector3 position = (Vector3)splineContainer.EvaluatePosition(t);
            Vector3 forward = (Vector3)splineContainer.EvaluateTangent(t);
            Vector3 up = (Vector3)splineContainer.EvaluateUpVector(t);
            Quaternion rotation = Quaternion.LookRotation(forward, up);

            // Spawn the object and save it directly into the array
            spawnedObjects[i] = Instantiate(prefabToSpawn, position, rotation);
            
            // Optional: Make them children of this object to keep the hierarchy clean
            spawnedObjects[i].transform.parent = this.transform;
        }
    }
}
