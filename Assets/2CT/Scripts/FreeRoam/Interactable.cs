using System.Collections;
using System.Collections.Generic;
using TwoCT.Combat;
using TwoCT.Core;
using TwoCT.Data;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// A press-E world interactable that plays dialogue. Solo dialogue plays only for the
    /// interacting player. Group dialogue requires ALL players standing near, then plays in sync
    /// for everyone — any one player's E advances it — and can trigger combat at the end
    /// (loads the combat scene). Networked so group flow is server-coordinated.
    /// </summary>
    /// <remarks>Requires a <see cref="NetworkObject"/> — the group flow uses ServerRpc/ClientRpc,
    /// which silently do nothing if the object isn't network-spawned. RequireComponent makes the
    /// editor add one automatically so a hand-placed interactable can't miss it.</remarks>
    [RequireComponent(typeof(NetworkObject))]
    public class Interactable : NetworkBehaviour
    {
        [Header("Dialogue")]
        public List<DialogueLine> lines = new List<DialogueLine>();
        [Tooltip("If true, every player must be within range and the dialogue plays for all at once.")]
        public bool requiresAllPlayers = false;
        public float interactRadius = 1.75f;

        [Tooltip("Characters allowed to start this conversation. Empty = anyone can. A player whose " +
                 "character isn't in this list never sees the interact prompt and can't initiate.")]
        public List<CharacterData> allowedInitiators = new List<CharacterData>();

        [Header("On complete")]
        [Tooltip("Load the (universal) combat scene when this dialogue finishes.")]
        public bool triggersCombat = false;
        public string combatScene = "Combat";
        [Tooltip("Which boss this fight loads. The combat scene is universal — it loads whichever boss " +
                 "the trigger selected. Leave empty to use the combat scene's own test boss.")]
        public BossData bossToFight;
        [Tooltip("Play only once: after the conversation finishes it's disabled — no prompt, can't be " +
                 "triggered again. (Synced to everyone; resets if the scene reloads, e.g. returning from combat.)")]
        public bool oneTimeOnly = false;

        [Header("On victory (applied when you return after WINNING this fight)")]
        [Tooltip("Unique key identifying this encounter. Leave empty to use the boss asset name. " +
                 "Completion persists for the run, so the world state below re-applies every time this scene loads.")]
        public string encounterId = "";
        [Tooltip("GameObjects to DISABLE once this encounter is beaten (e.g. a barrier, the boss gate).")]
        public List<GameObject> disableOnVictory = new List<GameObject>();
        [Tooltip("GameObjects to ENABLE once this encounter is beaten (e.g. a path, a new NPC).")]
        public List<GameObject> enableOnVictory = new List<GameObject>();
        [Tooltip("Also hide this interactable once its encounter is beaten, so the fight can't be re-triggered.")]
        public bool disableSelfOnVictory = true;

        /// <summary>Key used to track this encounter's completion — the explicit id, else the boss
        /// asset name, else this object's name.</summary>
        private string EncounterId =>
            !string.IsNullOrEmpty(encounterId) ? encounterId
            : bossToFight != null ? bossToFight.name : name;

        private bool _serverGroupActive;
        private int _serverIndex;
        private int _clientIndex = -1;      // line index this client is currently showing (for stale-advance guarding)
        private bool _subscribed;

        /// <summary>Set true once the conversation has played, when <see cref="oneTimeOnly"/>. Server-
        /// written, replicated so every client (and late joiners) stops offering the prompt.</summary>
        private readonly NetworkVariable<bool> _consumed = new(false);

        /// <summary>False when this is a spent one-time conversation — hides the prompt and blocks E.</summary>
        private bool Available => !(oneTimeOnly && _consumed.Value);

        /// <summary>A conversation that involves more than one person — the whole party
        /// (<see cref="requiresAllPlayers"/>) or two-plus specific characters
        /// (<see cref="allowedInitiators"/>) — so it must be gathered + synced across those clients.</summary>
        private bool IsGroupConversation =>
            requiresAllPlayers || (allowedInitiators != null && allowedInitiators.Count >= 2);

        // =====================================================================
        //  Local proximity + prompt + initiation
        // =====================================================================
        private bool LocalNear
        {
            get { var p = FreeRoamPlayer.Local; return p != null && Vector2.Distance(p.transform.position, transform.position) <= interactRadius; }
        }

        /// <summary>True if the LOCAL player's character is allowed to start this conversation
        /// (empty <see cref="allowedInitiators"/> = anyone).</summary>
        private bool LocalCanInitiate
        {
            get
            {
                if (allowedInitiators == null || allowedInitiators.Count == 0) return true;
                var pc = PlayerRegistry.Local;
                var ch = pc != null ? pc.Character : null;
                return ch != null && allowedInitiators.Contains(ch);
            }
        }

        private void Start() => ApplyEncounterState();

        /// <summary>On scene load, if this encounter has already been beaten this run, apply its
        /// persistent world state: disable/enable the listed entities and stop offering the fight.
        /// Purely local <c>SetActive</c> calls, driven by the replicated
        /// <see cref="SessionData.CompletedEncounters"/>, and re-applied on every load (the scene
        /// resets to its authored state on reload, so this re-runs from a clean baseline).
        /// NOTE: targets should generally be non-networked scene objects (walls, sprites, NPC roots);
        /// toggling in-scene NetworkObjects at runtime is not reliably supported by NGO.</summary>
        private void ApplyEncounterState()
        {
            if (!SessionData.IsEncounterComplete(EncounterId)) return;
            if (disableOnVictory != null)
                foreach (var go in disableOnVictory) if (go != null) go.SetActive(false);
            if (enableOnVictory != null)
                foreach (var go in enableOnVictory) if (go != null) go.SetActive(true);
            if (disableSelfOnVictory) enabled = false;   // no more prompt/trigger (keeps the NetworkObject intact)
        }

        private void Update()
        {
            var kb = Keyboard.current;
            bool ePressed = kb != null && kb.eKey.wasPressedThisFrame;
            if (!ePressed) return;
            if (DialogueBox.Instance.IsVisible) return; // box handles its own advancing
            if (!LocalNear || !LocalCanInitiate || !Available) return;

            // A multi-person conversation plays SYNCED: the server verifies every required person
            // is present, then drives the line reveal + advance for all their clients at once.
            // A single-person prompt is a local, solo conversation.
            if (IsGroupConversation) RequestGroupStartServerRpc();
            else PlaySolo();
        }

        /// <summary>Prompt for a group conversation. When specific characters are required, it names
        /// them ("Fiore & Leafy must gather") so players know who to round up; the whole-party case
        /// (<see cref="requiresAllPlayers"/>) stays generic.</summary>
        private string GroupPromptText()
        {
            if (!requiresAllPlayers && allowedInitiators != null && allowedInitiators.Count > 0)
            {
                var names = new List<string>();
                foreach (var c in allowedInitiators)
                    if (c != null) names.Add(string.IsNullOrEmpty(c.characterName) ? c.name : c.characterName);
                if (names.Count > 0) return $"Press E  —  {JoinNames(names)} must gather";
            }
            return "Press E  —  all players must gather";
        }

        /// <summary>"A", "A & B", or "A, B & C".</summary>
        private static string JoinNames(List<string> names)
        {
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return $"{names[0]} & {names[1]}";
            return string.Join(", ", names.GetRange(0, names.Count - 1)) + " & " + names[names.Count - 1];
        }

        private void PlaySolo()
        {
            DialogueBox.Instance.PlayLocal(lines, onComplete: () =>
            {
                if (oneTimeOnly) MarkConsumedServerRpc();
                if (triggersCombat) TriggerCombat();
            });
        }

        [ServerRpc(RequireOwnership = false)]
        private void MarkConsumedServerRpc() => _consumed.Value = true;

        /// <summary>
        /// Start combat. The host loads the scene DIRECTLY (no RPC) so it works even if this
        /// interactable's NetworkObject didn't spawn; a non-host client asks the server.
        /// </summary>
        private void TriggerCombat()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer) StartCoroutine(HostLoadCombat(nm));
            else RequestCombatServerRpc();
        }

        private IEnumerator HostLoadCombat(NetworkManager nm)
        {
            ScreenFader.FadeOut(0.5f);                                  // local fade (no RPC needed)
            if (nm.ConnectedClients.Count > 1) FadeClientRpc();        // fade remotes only when present
            yield return new WaitForSeconds(0.6f);
            SessionData.InRun = true;
            SessionData.ReturnScene = SceneManager.GetActiveScene().name;   // resume here after victory
            RecordEncounterSelection();
            if (nm.SceneManager != null) nm.SceneManager.LoadScene(combatScene, LoadSceneMode.Single);
        }

        /// <summary>Server-side: tell the universal combat scene which boss to load and tag the
        /// encounter so a win can be recorded (drives the post-victory enable/disable on return).</summary>
        private void RecordEncounterSelection()
        {
            SessionData.SelectedBossId = bossToFight != null ? bossToFight.name : null;
            SessionData.PendingEncounterId = EncounterId;
        }

        private void OnGUI()
        {
            if (DialogueBox.Instance.IsVisible || !LocalNear || !LocalCanInitiate || !Available) return;
            var cam = Camera.main; if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.2f);
            if (sp.z < 0) return;
            string prompt = IsGroupConversation ? GroupPromptText() : "Press E";

            int prevDepth = GUI.depth;
            GUI.depth = -1000;   // IMGUI draws last; a low depth keeps this above other OnGUI prompts
            var style = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 18 };
            const float w = 420f, h = 30f;
            var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h, w, h);   // centred on the point
            GUI.color = Color.black; GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), prompt, style); // shadow
            GUI.color = Color.white; GUI.Label(r, prompt, style);
            GUI.depth = prevDepth;
        }

        // =====================================================================
        //  Group flow (server-coordinated)
        // =====================================================================
        [ServerRpc(RequireOwnership = false)]
        private void RequestGroupStartServerRpc()
        {
            if (_serverGroupActive || lines.Count == 0) return;
            if (!AllRequiredNear()) { NotNearEnoughClientRpc(); return; }
            _serverGroupActive = true;
            _serverIndex = 0;
            BeginGroupClientRpc();
            ShowGroupLineClientRpc(0);
        }

        // Advance the whole party to the next line. Each client raises this ONLY once its own text
        // has finished typing (a press while still typing just completes that client's line locally,
        // in DialogueBox), so a single E on a line you've already read moves everyone on — no more
        // double-press. `fromIndex` is the line the caller was showing: near-simultaneous presses
        // from two players resolve to a single advance (the second is stale and ignored), so lines
        // never get skipped. Any participant's E drives it.
        [ServerRpc(RequireOwnership = false)]
        private void AdvanceGroupServerRpc(int fromIndex)
        {
            if (!_serverGroupActive || fromIndex != _serverIndex) return;
            _serverIndex++;
            if (_serverIndex < lines.Count)
            {
                ShowGroupLineClientRpc(_serverIndex);
            }
            else
            {
                _serverGroupActive = false;
                if (oneTimeOnly) _consumed.Value = true;
                EndGroupClientRpc();
                if (triggersCombat) StartCoroutine(ServerFadeThenLoad(combatScene, markRun: true));
            }
        }

        /// <summary>True when every person this conversation requires is present and within range:
        /// the whole party for <see cref="requiresAllPlayers"/>, or a live+near player for EACH
        /// character in <see cref="allowedInitiators"/>.</summary>
        private bool AllRequiredNear()
        {
            if (requiresAllPlayers || allowedInitiators == null || allowedInitiators.Count == 0)
                return AllPlayersNear();

            foreach (var required in allowedInitiators)
            {
                if (required == null) continue;
                bool present = false;
                foreach (var kv in NetworkManager.ConnectedClients)
                {
                    var po = kv.Value.PlayerObject;
                    var pc = po != null ? po.GetComponent<PlayerCombatant>() : null;
                    if (pc == null || pc.Character != required) continue;
                    if (Vector2.Distance(po.transform.position, transform.position) > interactRadius) return false; // in game but not gathered
                    present = true;
                    break;
                }
                if (!present) return false;   // that required character isn't even in this session
            }
            return true;
        }

        private bool AllPlayersNear()
        {
            foreach (var kv in NetworkManager.ConnectedClients)
            {
                var po = kv.Value.PlayerObject;
                var frp = po != null ? po.GetComponent<FreeRoamPlayer>() : null;
                if (frp == null) return false;
                if (Vector2.Distance(frp.transform.position, transform.position) > interactRadius) return false;
            }
            return NetworkManager.ConnectedClients.Count > 0;
        }

        [ClientRpc]
        private void BeginGroupClientRpc()
        {
            DialogueBox.Instance.BeginExternal();
            if (!_subscribed) { DialogueBox.Instance.ExternalSkipPressed += OnGroupSkip; _subscribed = true; }
        }

        [ClientRpc]
        private void ShowGroupLineClientRpc(int index)
        {
            if (index < 0 || index >= lines.Count) return;
            _clientIndex = index;   // remember which line we're on, to tag our advance requests
            var line = lines[index];
            line.FireActions();   // each client runs the swap/remove from its identical scene lines list
            DialogueBox.Instance.ShowExternalLine(line.speaker, line.text);
        }

        [ClientRpc]
        private void EndGroupClientRpc()
        {
            DialogueBox.Instance.EndExternal();
            if (_subscribed) { DialogueBox.Instance.ExternalSkipPressed -= OnGroupSkip; _subscribed = false; }
        }

        [ClientRpc]
        private void NotNearEnoughClientRpc()
        {
            // Lightweight feedback; a real UI toast can replace this.
            Debug.Log("[2CT] All players must gather to start this conversation.");
        }

        private void OnGroupSkip() => AdvanceGroupServerRpc(_clientIndex);

        // =====================================================================
        //  Combat / scene transition (server)
        // =====================================================================
        [ServerRpc(RequireOwnership = false)]
        private void RequestCombatServerRpc() => StartCoroutine(ServerFadeThenLoad(combatScene, markRun: true));

        private IEnumerator ServerFadeThenLoad(string scene, bool markRun)
        {
            FadeClientRpc();
            yield return new WaitForSeconds(0.6f);
            if (markRun) SessionData.InRun = true;
            SessionData.ReturnScene = SceneManager.GetActiveScene().name;   // resume here after victory
            RecordEncounterSelection();
            if (NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }

        [ClientRpc]
        private void FadeClientRpc() => ScreenFader.FadeOut(0.5f);

        public override void OnNetworkDespawn()
        {
            if (_subscribed && DialogueBox.Instance != null)
            { DialogueBox.Instance.ExternalSkipPressed -= OnGroupSkip; _subscribed = false; }
        }

        // Always-visible marker so interactables are easy to place/find in the Scene view.
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, interactRadius);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.12f);
            Gizmos.DrawSphere(transform.position, interactRadius);
            Gizmos.color = new Color(1f, 0.7f, 0.1f, 1f);
            Gizmos.DrawSphere(transform.position, 0.12f);   // exact centre
#if UNITY_EDITOR
            string who = (allowedInitiators != null && allowedInitiators.Count > 0)
                ? "Talk (" + string.Join(", ", allowedInitiators.ConvertAll(c => c != null ? c.name : "?")) + ")"
                : "Talk (anyone)";
            UnityEditor.Handles.color = new Color(1f, 0.85f, 0.2f, 1f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * (interactRadius + 0.2f), who);
#endif
        }
    }
}
