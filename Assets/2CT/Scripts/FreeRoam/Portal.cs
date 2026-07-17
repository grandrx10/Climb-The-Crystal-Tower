using System.Collections;
using TwoCT.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// A doorway that teleports the whole party to another scene once EVERY player is standing on
    /// it at the same time. A live "X / Y players" prompt floats above the door so the group knows
    /// how many still need to gather. Server-authoritative: the host counts who is within range,
    /// replicates the tally for the prompt, and performs the networked scene load when everyone has
    /// arrived (after a short debounce so brushing past it doesn't fire).
    /// </summary>
    public class Portal : NetworkBehaviour
    {
        [SerializeField] private string targetScene = "Combat";
        [SerializeField] private float radius = 1.5f;
        [SerializeField] private bool markRunStarted = false;
        [Tooltip("Seconds all players must stand on the pad together before it fires (debounce).")]
        [SerializeField] private float confirmSeconds = 0.35f;

        // Replicated so every client renders the same "X / Y" prompt.
        private readonly NetworkVariable<int> _onPad = new(0);
        private readonly NetworkVariable<int> _needed = new(0);

        private bool _loading;
        private float _allPresentSince = -1f;

        private void Update()
        {
            if (IsServer) ServerTick();
        }

        // Count players within range, replicate the tally, and load once everyone has gathered.
        private void ServerTick()
        {
            if (_loading) return;
            int total = NetworkManager.ConnectedClients.Count;
            int on = 0;
            foreach (var kv in NetworkManager.ConnectedClients)
            {
                var po = kv.Value.PlayerObject;
                if (po == null) continue;
                if (Vector2.Distance((Vector2)po.transform.position, (Vector2)transform.position) <= radius) on++;
            }
            _needed.Value = total;
            _onPad.Value = on;

            if (total > 0 && on >= total)
            {
                if (_allPresentSince < 0f) _allPresentSince = Time.time;
                if (Time.time - _allPresentSince >= Mathf.Max(0f, confirmSeconds))
                {
                    _loading = true;
                    StartCoroutine(FadeThenLoad());
                }
            }
            else _allPresentSince = -1f;
        }

        private IEnumerator FadeThenLoad()
        {
            FadeClientRpc();
            yield return new WaitForSeconds(0.6f);
            if (markRunStarted) SessionData.InRun = true;
            if (NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
        }

        [ClientRpc]
        private void FadeClientRpc() => ScreenFader.FadeOut(0.5f);

        private void OnGUI()
        {
            var cam = Camera.main; if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.2f);
            if (sp.z < 0) return;

            int total = Mathf.Max(_needed.Value, 1);
            int on = _onPad.Value;
            string msg = on >= total ? "Entering..." : $"Gather all players   {on} / {total}";
            var style = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(sp.x - 110, Screen.height - sp.y - 26, 220, 26), msg, style);
        }

        // Always-visible marker so portals are easy to place/find in the Scene view (mirrors Interactable).
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, radius);            // gather radius
            Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.12f);
            Gizmos.DrawSphere(transform.position, radius);               // soft fill
            Gizmos.color = new Color(0.75f, 0.55f, 1f, 1f);
            Gizmos.DrawSphere(transform.position, 0.12f);                // exact centre
#if UNITY_EDITOR
            string label = "Portal → " + (string.IsNullOrEmpty(targetScene) ? "(no scene set)" : targetScene);
            UnityEditor.Handles.color = new Color(0.75f, 0.55f, 1f, 1f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * (radius + 0.2f), label);
#endif
        }
    }
}
