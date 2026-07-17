using TwoCT.Combat;
using TwoCT.Core;
using Unity.Netcode;
using UnityEngine;

namespace TwoCT.Net
{
    /// <summary>
    /// Zero-wiring dev panel (IMGUI). Drop it in the combat scene and press Play: Host / Join,
    /// then "Begin Encounter" to start the fight solo or with joined players. Purely a
    /// development aid — the real lobby UI replaces it later.
    /// </summary>
    public class DevControlPanel : MonoBehaviour
    {
        [SerializeField] private string joinAddress = "127.0.0.1";
        private bool _open = true;

        private void OnGUI()
        {
            var nm = NetworkManager.Singleton;

            // Always-visible toggle so the panel can be tucked away when it's in the way.
            if (GUI.Button(new Rect(10, 10, 130, 24), _open ? "▼ Dev Panel" : "▶ Dev Panel"))
                _open = !_open;
            if (!_open) return;

            if (nm == null) { GUI.Label(new Rect(10, 40, 400, 20), "No NetworkManager in scene."); return; }

            GUILayout.BeginArea(new Rect(10, 40, 240, 280), GUI.skin.box);
            GUILayout.Label("<b>2CT Dev Panel</b>", Rich());

            // Debug view: draw true collision shapes (hitbox ≠ displayed sprite) in combat AND free roam.
            DebugView.ShowHitboxes = GUILayout.Toggle(DebugView.ShowHitboxes, " Show hitboxes");

            if (!nm.IsClient && !nm.IsServer)
            {
                if (GUILayout.Button("Host (solo OK)")) ConnectionManager.Instance?.StartHost();
                joinAddress = GUILayout.TextField(joinAddress);
                if (GUILayout.Button("Join")) ConnectionManager.Instance?.StartClient(joinAddress);
            }
            else
            {
                GUILayout.Label($"Role: {(nm.IsHost ? "Host" : nm.IsServer ? "Server" : "Client")}");
                GUILayout.Label($"Players: {nm.ConnectedClients.Count}/{ConnectionManager.MaxPlayers}");

                if (nm.IsServer && CombatManager.Instance != null)
                {
                    if (!CombatManager.Instance.CombatRunning)
                    {
                        if (GUILayout.Button("Begin Encounter"))
                            CombatManager.Instance.ServerBeginTestEncounter();
                    }
                    else
                    {
                        GUILayout.Label($"Phase: {CombatManager.Instance.Phase.Value}");
                        GUILayout.Label($"Round: {CombatManager.Instance.RoundNumber.Value}");
                    }
                }

                if (GUILayout.Button("Shutdown")) ConnectionManager.Instance?.Shutdown();
            }
            GUILayout.EndArea();
        }

        private static GUIStyle Rich()
        {
            var s = new GUIStyle(GUI.skin.label) { richText = true };
            return s;
        }
    }
}
