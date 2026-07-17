using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace TwoCT.Net
{
    /// <summary>
    /// Thin wrapper over NGO connection lifecycle. For this prototype it drives direct
    /// host/client connections (localhost by default) and enforces the 3-player cap. Lobbies
    /// may start with a single player. Relay/UGS Lobby slot in here later behind the same API.
    /// </summary>
    public class ConnectionManager : MonoBehaviour
    {
        public const int MaxPlayers = 3;

        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        public static ConnectionManager Instance { get; private set; }
        public event Action<int> PlayerCountChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.ConnectionApprovalCallback += ApproveConnection;
            nm.OnClientConnectedCallback += _ => PlayerCountChanged?.Invoke(nm.ConnectedClients.Count);
            nm.OnClientDisconnectCallback += _ => PlayerCountChanged?.Invoke(nm.ConnectedClients.Count);
        }

        private void ConfigureTransport()
        {
            var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (utp != null) utp.SetConnectionData(address, port);
        }

        /// <summary>Approval gate: hard-caps the lobby at 3 players (host counts as one).</summary>
        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
        {
            bool hasRoom = NetworkManager.Singleton.ConnectedClients.Count < MaxPlayers;
            res.Approved = hasRoom;
            res.CreatePlayerObject = hasRoom;   // auto-spawns the Player Prefab (a PlayerCombatant)
            res.Reason = hasRoom ? "" : "Lobby full (max 3 players).";
        }

        public bool StartHost()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return false;
            if (nm.IsListening || nm.IsServer || nm.IsClient)
            {
                Debug.LogWarning("[2CT] StartHost ignored — already hosting/connected. Leave first.");
                return false;
            }
            ConfigureTransport();
            bool ok = nm.StartHost();                       // solo host is allowed
            if (!ok)
                Debug.LogError($"[2CT] StartHost failed to bind port {port}. It's likely still held " +
                               "by a previous play session — restart the Unity Editor (or enable " +
                               "Enter Play Mode ▸ Reload Domain) to free the socket.");
            return ok;
        }

        public bool StartClient(string overrideAddress = null)
        {
            if (!string.IsNullOrEmpty(overrideAddress)) address = overrideAddress;
            ConfigureTransport();
            return NetworkManager.Singleton.StartClient();
        }

        public void Shutdown() => NetworkManager.Singleton.Shutdown();

        // Guarantee the transport socket is released when play/app ends. Without this a host that
        // exits play mode can leave port 7777 bound inside the editor process, so the next
        // StartHost fails to bind until a domain reload or editor restart.
        private void OnApplicationQuit()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsListening || nm.IsServer || nm.IsClient)) nm.Shutdown();
        }
    }
}
