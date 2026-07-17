using System.Collections.Generic;
using TwoCT.Bullets;
using TwoCT.Combat;
using TwoCT.Data;
using TwoCT.FreeRoam;
using TwoCT.Lobby;
using TwoCT.Net;
using TwoCT.UI;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Builds the three playable scenes into the project's <c>Assets/Scenes</c> folder, all wired
    /// and sharing one persistent NetworkManager (spawned by NetworkBootstrap). Flow:
    /// Lobby → (Start) → Level → (portal / group dialogue) → Combat.
    /// Each scene is also independently openable for testing.
    /// </summary>
    public static class SceneBuilders
    {
        private const string Lobby = SceneBuildCommon.SceneDir + "/Lobby.unity";
        private const string Level = SceneBuildCommon.SceneDir + "/Level.unity";
        private const string Combat = SceneBuildCommon.SceneDir + "/Combat.unity";

        // =====================================================================
        //  Menu entry points
        // =====================================================================
        [MenuItem("2CT/Scenes/Build All Scenes", priority = 30)]
        public static void BuildAll()
        {
            var (nmPrefab, boss, characters) = Prep();
            BuildLobbyScene(nmPrefab);
            BuildLevelScene(nmPrefab);
            BuildCombatScene(nmPrefab, boss, characters);
            SceneBuildCommon.AddSceneToBuild(Lobby, makeFirst: true);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("2CT",
                "Built Lobby, Level and Combat into Assets/Scenes.\n\nOpen Lobby.unity, press Play, Create Lobby (Host), pick a character, Ready, START RUN.\n(Each scene also runs standalone via its dev panel.)", "OK");
        }

        [MenuItem("2CT/Scenes/Build Lobby Scene", priority = 31)]
        public static void MenuLobby() { var p = Prep(); BuildLobbyScene(p.nmPrefab); SceneBuildCommon.AddSceneToBuild(Lobby, true); Done(Lobby); }

        [MenuItem("2CT/Scenes/Build Level Scene", priority = 32)]
        public static void MenuLevel() { var p = Prep(); BuildLevelScene(p.nmPrefab); Done(Level); }

        [MenuItem("2CT/Scenes/Build Combat Test Scene", priority = 33)]
        public static void MenuCombat() { var p = Prep(); BuildCombatScene(p.nmPrefab, p.boss, p.characters); Done(Combat); }

        private static void Done(string path) => EditorUtility.DisplayDialog("2CT", "Built " + path, "OK");

        /// <summary>
        /// Stamp the free-roam essentials into the CURRENTLY-OPEN scene without wiping your art:
        /// an orthographic Main Camera that follows the player, the persistent NetworkManager spawner,
        /// a FreeRoamContext (+ spawn), a DialogueBox, and the dev panel. Each is added only if missing.
        /// Use this to turn a hand-made scene (e.g. "First Boss area") into a working free-roam level —
        /// scene objects (incl. the camera) never carry over a LoadSceneMode.Single scene swap.
        /// </summary>
        [MenuItem("2CT/Scenes/Add Free-Roam Essentials (current scene)", priority = 34)]
        public static void AddFreeRoamEssentials()
        {
            var (nmPrefab, _, _) = Prep();
            var added = new List<string>();

            // Camera: orthographic + CameraFollow (which auto-finds the local player + context, no wiring).
            var cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                var camGO = new GameObject("Main Camera");
                cam = camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
                added.Add("Main Camera");
            }
            if (!cam.CompareTag("MainCamera")) cam.tag = "MainCamera";   // Camera.main is used elsewhere
            cam.orthographic = true;
            if (cam.orthographicSize < 0.01f) cam.orthographicSize = 5.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            var cp = cam.transform.position; cp.z = -10f; cam.transform.position = cp;
            if (cam.GetComponent<CameraFollow>() == null) { cam.gameObject.AddComponent<CameraFollow>(); added.Add("CameraFollow"); }

            // Persistent NetworkManager spawner (the one thing that DOES carry across scenes).
            if (Object.FindFirstObjectByType<NetworkBootstrap>() == null) { SceneBuildCommon.AddNetworkBootstrap(nmPrefab); added.Add("NetworkBootstrap"); }

            // Free-roam context: movement bounds, camera clamp, and the default spawn point.
            if (Object.FindFirstObjectByType<FreeRoamContext>() == null)
            {
                var ctxGO = new GameObject("FreeRoamContext");
                var ctx = ctxGO.AddComponent<FreeRoamContext>();
                ctx.boundsSize = new Vector2(44, 9);
                var spawn = new GameObject("Spawn");
                spawn.transform.SetParent(ctxGO.transform, false);
                spawn.transform.position = new Vector3(-16, -2.5f, 0);
                ctx.spawnPoint = spawn.transform;
                added.Add("FreeRoamContext (+ Spawn)");
            }

            // Dialogue box for any Interactables in the scene.
            if (Object.FindFirstObjectByType<DialogueBox>() == null)
            {
                var dlg = new GameObject("DialogueBox").AddComponent<DialogueBox>();
                dlg.BuildInEditor();
                EditorUtility.SetDirty(dlg);
                added.Add("DialogueBox");
            }

            if (Object.FindFirstObjectByType<DevControlPanel>() == null) { new GameObject("DevControlPanel").AddComponent<DevControlPanel>(); added.Add("DevControlPanel"); }

            // Save + refresh in-scene NetworkObject hashes (portals/interactables you placed need valid,
            // unique hashes or their RPCs silently do nothing) + add to Build Settings so it can load.
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.SaveScene(scene, scene.path);
                SceneBuildCommon.RefreshSceneNetworkObjectHashes();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, scene.path);
                SceneBuildCommon.AddSceneToBuild(scene.path);
            }

            EditorUtility.DisplayDialog("2CT",
                (added.Count == 0 ? "Scene already had all free-roam essentials." : "Added:\n • " + string.Join("\n • ", added)) +
                (string.IsNullOrEmpty(scene.path) ? "\n\nSave the scene, then run this again so its NetworkObject hashes get refreshed." : "\n\nSaved + added to Build Settings."),
                "OK");
        }

        // =====================================================================
        //  Shared prep: content + prefabs
        // =====================================================================
        private static (GameObject nmPrefab, BossData boss, List<CharacterData> characters) Prep()
        {
            var boss = SceneBuildCommon.FindAsset<BossData>("Boss_FirstGuardian");
            if (boss == null) { ContentTools.GenerateSampleContent(); boss = SceneBuildCommon.FindAsset<BossData>("Boss_FirstGuardian"); }
            var characters = new List<CharacterData>
            {
                SceneBuildCommon.FindAsset<CharacterData>("Fiore"),
                SceneBuildCommon.FindAsset<CharacterData>("Wylta"),
                SceneBuildCommon.FindAsset<CharacterData>("Leafy"),
            };
            var playerPrefab = SceneBuildCommon.BuildPlayerPrefab();
            var nmPrefab = SceneBuildCommon.BuildNetworkManagerPrefab(playerPrefab);
            return (nmPrefab, boss, characters);
        }

        private static void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        private static void Save(string path)
        {
            SceneBuildCommon.EnsureFolder(SceneBuildCommon.SceneDir);
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            // 1) Save so scene-placed NetworkObjects get valid fileIDs.
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            // 2) Compute their (now unique, non-zero) GlobalObjectIdHash and 3) persist it.
            SceneBuildCommon.RefreshSceneNetworkObjectHashes();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            SceneBuildCommon.AddSceneToBuild(path);
        }

        private static Camera SetupCamera(float size, Vector3 pos, Color bg)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = true; cam.orthographicSize = size;
                cam.transform.position = pos; cam.backgroundColor = bg; cam.clearFlags = CameraClearFlags.SolidColor;
            }
            return cam;
        }

        // =====================================================================
        //  Lobby scene
        // =====================================================================
        private static void BuildLobbyScene(GameObject nmPrefab)
        {
            NewScene();
            SetupCamera(5.5f, new Vector3(0, 0, -10), new Color(0.05f, 0.04f, 0.08f));
            var white = SceneBuildCommon.EnsureWhiteSprite();

            SceneBuildCommon.NewSprite("Backdrop", new Vector3(0, 0, 5), new Vector3(200, 120, 1), new Color(0.10f, 0.09f, 0.16f), -100, white);
            SceneBuildCommon.AddNetworkBootstrap(nmPrefab);

            // Lobby controller (in-scene NetworkObject) + IMGUI panel.
            var lc = new GameObject("LobbyController");
            lc.AddComponent<NetworkObject>();
            lc.AddComponent<LobbyController>();
            var lobbyUI = new GameObject("LobbyUI").AddComponent<LobbyUI>();
            lobbyUI.BuildInEditor();
            EditorUtility.SetDirty(lobbyUI);

            Save(Lobby);
        }

        // =====================================================================
        //  Level scene (free roam)
        // =====================================================================
        private static void BuildLevelScene(GameObject nmPrefab)
        {
            NewScene();
            var cam = SetupCamera(5.5f, new Vector3(0, 0, -10), new Color(0.07f, 0.10f, 0.14f));
            if (cam != null) cam.gameObject.AddComponent<CameraFollow>();
            var white = SceneBuildCommon.EnsureWhiteSprite();

            SceneBuildCommon.AddNetworkBootstrap(nmPrefab);
            new GameObject("DevControlPanel").AddComponent<DevControlPanel>();

            // Parallax background layers + ground.
            SceneBuildCommon.NewSprite("BG_Far", new Vector3(0, 2, 20), new Vector3(120, 60, 1), new Color(0.10f, 0.14f, 0.22f), -50, white)
                .gameObject.AddComponent<ParallaxLayer>().factor = 0.2f;
            SceneBuildCommon.NewSprite("BG_Mid", new Vector3(0, 0, 12), new Vector3(120, 30, 1), new Color(0.13f, 0.18f, 0.26f), -40, white)
                .gameObject.AddComponent<ParallaxLayer>().factor = 0.5f;
            SceneBuildCommon.NewSprite("Ground", new Vector3(0, -4.2f, 1), new Vector3(120, 4, 1), new Color(0.18f, 0.16f, 0.20f), -10, white);

            // Free-roam context + spawn.
            var ctxGO = new GameObject("FreeRoamContext");
            var ctx = ctxGO.AddComponent<FreeRoamContext>();
            ctx.boundsSize = new Vector2(44, 9);
            var spawn = new GameObject("Spawn");
            spawn.transform.SetParent(ctxGO.transform, false);
            spawn.transform.position = new Vector3(-16, -2.5f, 0);
            ctx.spawnPoint = spawn.transform;

            // A solo sign (talk only).
            var sign = MakeInteractable("Sign_Solo", new Vector3(-8, -2.5f, 0), new Color(0.7f, 0.7f, 0.3f), white);
            sign.requiresAllPlayers = false; sign.triggersCombat = false;
            sign.lines = new List<DialogueLine>
            {
                new DialogueLine { speaker = "Sign", text = "The Crystal Tower looms ahead. Gather your party at the gate.", autoAdvanceSeconds = 0 },
            };

            // Boss-room gate: press E → brief line → combat. Solo-friendly (works with 1 player)
            // and host loads combat directly, so it doesn't depend on networked RPC plumbing.
            var gate = MakeInteractable("Gate_BossRoom", new Vector3(4, -2.5f, 0), new Color(0.9f, 0.4f, 0.4f), white);
            gate.requiresAllPlayers = false; gate.triggersCombat = true; gate.combatScene = "Combat";
            gate.lines = new List<DialogueLine>
            {
                new DialogueLine { speaker = "", text = "You feel something is wrong...", autoAdvanceSeconds = 0 },
            };

            // A direct door to combat.
            var portalGO = new GameObject("Portal_ToCombat");
            portalGO.transform.position = new Vector3(18, -2.5f, 0);
            portalGO.AddComponent<SpriteRenderer>().sprite = white;
            var psr = portalGO.GetComponent<SpriteRenderer>();
            psr.color = new Color(0.6f, 0.4f, 1f); psr.sortingOrder = 5; portalGO.transform.localScale = new Vector3(1.2f, 2.4f, 1);
            portalGO.AddComponent<NetworkObject>();
            portalGO.AddComponent<Portal>();

            // Dialogue box (editor-built; also used by the combat scene's boss lines is separate).
            var dlg = new GameObject("DialogueBox").AddComponent<DialogueBox>();
            dlg.BuildInEditor();
            EditorUtility.SetDirty(dlg);

            Save(Level);
        }

        private static Interactable MakeInteractable(string name, Vector3 pos, Color color, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.color = color; sr.sortingOrder = 5;
            go.transform.localScale = new Vector3(1f, 1.6f, 1f);
            go.AddComponent<NetworkObject>();
            return go.AddComponent<Interactable>();
        }

        // =====================================================================
        //  Combat scene
        // =====================================================================
        private static void BuildCombatScene(GameObject nmPrefab, BossData boss, List<CharacterData> characters)
        {
            NewScene();
            SetupCamera(5.5f, new Vector3(0, 0, -10), new Color(0.06f, 0.06f, 0.1f));
            var white = SceneBuildCommon.EnsureWhiteSprite();

            SceneBuildCommon.AddNetworkBootstrap(nmPrefab);
            new GameObject("DevControlPanel").AddComponent<DevControlPanel>();

            // Boss (in-scene NetworkObject) + muzzle. Uses a persisted sprite (survives scene save).
            var bossGO = new GameObject("Boss");
            bossGO.transform.position = new Vector3(5f, 0.5f, 0);
            // Match the player avatars (which force localScale = 1 every frame): the boss art is
            // authored at the same import scale, so leaving it at 1 keeps its on-screen size in
            // line with the players. Bump the sprite's own PPU/pixel size for a bigger boss.
            bossGO.transform.localScale = Vector3.one;
            var bsr = bossGO.AddComponent<SpriteRenderer>();
            bsr.sprite = white; bsr.color = new Color(0.7f, 0.2f, 0.3f);
            bossGO.AddComponent<NetworkObject>();
            var bossCtrl = bossGO.AddComponent<BossController>();
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(bossGO.transform, false);
            // Boss sits at y=0.5, arena centre at y=-0.5 — offset the muzzle down so bullets emit
            // level with the box's vertical centre (CombatManager also enforces this at runtime).
            muzzle.transform.localPosition = new Vector3(-0.6f, -1.0f, 0f);

            // Arena + bullet system.
            var arenaGO = new GameObject("DefendArena");
            arenaGO.transform.position = new Vector3(-0.5f, -0.5f, 0);
            var arena = arenaGO.AddComponent<DefendArena>();
            var bulletGO = new GameObject("BulletSystem");
            var bulletSystem = bulletGO.AddComponent<BulletSystem>();
            SceneBuildCommon.SetPrivate(bulletSystem, "arena", arena);

            // CombatManager (in-scene NetworkObject) + wiring.
            var cmGO = new GameObject("CombatManager");
            cmGO.AddComponent<NetworkObject>();
            var cm = cmGO.AddComponent<CombatManager>();
            SceneBuildCommon.SetPrivate(cm, "boss", bossCtrl);
            SceneBuildCommon.SetPrivate(cm, "bulletSystem", bulletSystem);
            SceneBuildCommon.SetPrivate(cm, "bossMuzzle", muzzle.transform);
            SceneBuildCommon.SetPrivate(cm, "arena", arena);
            SceneBuildCommon.SetPrivate(cm, "testBoss", boss);
            SceneBuildCommon.SetPrivateList(cm, "defaultCharacters", characters);

            var hud = new GameObject("CombatHUD").AddComponent<CombatHUD>();
            hud.BuildInEditor();
            EditorUtility.SetDirty(hud);

            var cardPrefab = SceneBuildCommon.BuildCardPrefab();
            var hand = new GameObject("HandView").AddComponent<HandView>();
            hand.BuildInEditor();
            SceneBuildCommon.SetPrivate(hand, "cardPrefab", cardPrefab);
            EditorUtility.SetDirty(hand);

            Save(Combat);
        }
    }
}
