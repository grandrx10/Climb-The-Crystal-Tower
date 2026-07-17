using TwoCT.Combat;
using TwoCT.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TwoCT.Lobby
{
    /// <summary>One player's lobby state. Replicated in the LobbyController's NetworkList.</summary>
    public struct LobbySlot : INetworkSerializable, System.IEquatable<LobbySlot>
    {
        public ulong clientId;
        public int character;   // index into ContentRegistry.characters, -1 = none
        public bool ready;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref clientId);
            s.SerializeValue(ref character);
            s.SerializeValue(ref ready);
        }

        public bool Equals(LobbySlot o) => clientId == o.clientId && character == o.character && ready == o.ready;
    }

    /// <summary>
    /// Server-authoritative lobby: tracks each connected player's chosen character (enforcing
    /// uniqueness) and ready state, and starts the run (writes SessionData + loads the first
    /// level) when the host presses Start and everyone is ready. Scene object in the Lobby scene.
    /// </summary>
    public class LobbyController : NetworkBehaviour
    {
        public static LobbyController Instance { get; private set; }

        private readonly NetworkList<LobbySlot> _slots = new NetworkList<LobbySlot>();
        public NetworkList<LobbySlot> Slots => _slots;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                foreach (var id in NetworkManager.ConnectedClientsIds) AddSlot(id);
                NetworkManager.OnClientConnectedCallback += AddSlot;
                NetworkManager.OnClientDisconnectCallback += RemoveSlot;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback -= AddSlot;
                NetworkManager.OnClientDisconnectCallback -= RemoveSlot;
            }
            if (Instance == this) Instance = null;
        }

        private void AddSlot(ulong clientId)
        {
            if (IndexOf(clientId) >= 0) return;
            _slots.Add(new LobbySlot { clientId = clientId, character = -1, ready = false });
        }

        private void RemoveSlot(ulong clientId)
        {
            int i = IndexOf(clientId);
            if (i >= 0) _slots.RemoveAt(i);
        }

        private int IndexOf(ulong clientId)
        {
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].clientId == clientId) return i;
            return -1;
        }

        private bool CharacterTaken(int character, ulong exceptClient)
        {
            if (character < 0) return false;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].character == character && _slots[i].clientId != exceptClient) return true;
            return false;
        }

        // =====================================================================
        //  Client requests
        // =====================================================================
        [ServerRpc(RequireOwnership = false)]
        public void SelectCharacterServerRpc(int character, ServerRpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            int i = IndexOf(sender);
            if (i < 0) return;
            if (CharacterTaken(character, sender)) return;       // uniqueness enforced
            var slot = _slots[i];
            slot.character = character;
            _slots[i] = slot;
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetReadyServerRpc(bool ready, ServerRpcParams rpc = default)
        {
            int i = IndexOf(rpc.Receive.SenderClientId);
            if (i < 0) return;
            var slot = _slots[i];
            if (ready && slot.character < 0) return;             // must pick before readying
            slot.ready = ready;
            _slots[i] = slot;
        }

        [ServerRpc(RequireOwnership = false)]
        public void StartRunServerRpc(ServerRpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId != NetworkManager.ServerClientId) return; // host only
            if (_slots.Count == 0) return;
            foreach (var s in _slots) if (!s.ready || s.character < 0) return;

            SessionData.Reset();
            SessionData.InRun = true;
            foreach (var s in _slots) SessionData.CharacterByClient[s.clientId] = s.character;

            // Push each pick onto the persistent player object now, so the free-roam avatar can
            // show the right sprite in the Level scene (this NetworkVariable replicates to all
            // clients and survives the scene load into combat, where it's set again identically).
            foreach (var s in _slots)
                if (NetworkManager.ConnectedClients.TryGetValue(s.clientId, out var cc) && cc.PlayerObject != null)
                {
                    var pc = cc.PlayerObject.GetComponent<PlayerCombatant>();
                    if (pc != null) pc.CharacterIndex.Value = s.character;
                }

            NetworkManager.SceneManager.LoadScene(SessionData.FirstLevelScene, LoadSceneMode.Single);
        }

        public bool AllReady()
        {
            if (_slots.Count == 0) return false;
            foreach (var s in _slots) if (!s.ready || s.character < 0) return false;
            return true;
        }
    }
}
