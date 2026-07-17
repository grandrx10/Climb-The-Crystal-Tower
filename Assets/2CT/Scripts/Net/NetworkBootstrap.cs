using Unity.Netcode;
using UnityEngine;

namespace TwoCT.Net
{
    /// <summary>
    /// Guarantees exactly one persistent NetworkManager. Each built scene contains a
    /// NetworkBootstrap; on load it spawns the shared NetworkManager prefab (marked
    /// DontDestroyOnLoad) only if one doesn't already exist. This lets you open any scene
    /// standalone for testing AND flow lobby → level → combat without duplicate managers.
    /// </summary>
    public class NetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject networkManagerPrefab;

        private void Awake()
        {
            if (NetworkManager.Singleton != null) return;      // came from a previous scene — reuse it
            if (networkManagerPrefab == null)
            {
                Debug.LogError("[2CT] NetworkBootstrap has no NetworkManager prefab assigned.");
                return;
            }
            var go = Instantiate(networkManagerPrefab);
            go.name = "NetworkManager";
            DontDestroyOnLoad(go);
        }
    }
}
