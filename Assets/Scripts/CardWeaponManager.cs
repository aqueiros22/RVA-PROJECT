using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class CardWeaponManager : MonoBehaviour
{
    public ARTrackedImageManager trackedImageManager;

    [Header("Prefabs")]
    public GameObject swordPrefab;
    public GameObject axePrefab;

    [Header("Proportional Scale (Same proportion as Knight's hand)")]
    // Adjust this value to match the knight model's scale
    public Vector3 proportionalWeaponScale = new Vector3(0.5f, 0.5f, 0.5f);
    public float heightAboveCard = 0.03f; // 3cm above card

    private Dictionary<string, GameObject> spawnedWeapons = new Dictionary<string, GameObject>();

    void OnEnable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
        }
    }

    void OnDisable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
        }
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        foreach (var trackedImage in eventArgs.added)
        {
            UpdateWeapon(trackedImage);
        }

        foreach (var trackedImage in eventArgs.updated)
        {
            UpdateWeapon(trackedImage);
        }

        foreach (var trackedImage in eventArgs.removed)
        {
            if (spawnedWeapons.ContainsKey(trackedImage.referenceImage.name))
            {
                Destroy(spawnedWeapons[trackedImage.referenceImage.name]);
                spawnedWeapons.Remove(trackedImage.referenceImage.name);
            }
        }
    }

    private void UpdateWeapon(ARTrackedImage trackedImage)
    {
        string imageName = trackedImage.referenceImage.name;
        
        // Also keep the weapon visible if tracking is Limited (often happens when too close)
        if (trackedImage.trackingState == TrackingState.Tracking || trackedImage.trackingState == TrackingState.Limited)
        {
            if (!spawnedWeapons.ContainsKey(imageName))
            {
                GameObject prefabToInstantiate = null;
                
                // Logic to select prefab based on tracked image name
                if (imageName.ToLower().Contains("sword") || imageName.ToLower().Contains("espada"))
                {
                    prefabToInstantiate = swordPrefab;
                }
                else if (imageName.ToLower().Contains("axe") || imageName.ToLower().Contains("machado"))
                {
                    prefabToInstantiate = axePrefab;
                }

                if (prefabToInstantiate != null)
                {
                    GameObject weapon = Instantiate(prefabToInstantiate, trackedImage.transform);
                    weapon.transform.localPosition = new Vector3(0, heightAboveCard, 0);
                    weapon.transform.localScale = proportionalWeaponScale;
                    spawnedWeapons[imageName] = weapon;
                }
            }
            else
            {
                GameObject weapon = spawnedWeapons[imageName];
                weapon.SetActive(true);
            }
        }
        else
        {
            // Hide the weapon when the image tracking is lost or limited
            if (spawnedWeapons.ContainsKey(imageName))
            {
                spawnedWeapons[imageName].SetActive(false);
            }
        }
    }
}