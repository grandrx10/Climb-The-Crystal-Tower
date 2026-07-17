using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TwoCT.Bullets;
using TwoCT.Combat;
using TwoCT.FreeRoam;
using TwoCT.Net;
using TwoCT.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Shared building blocks for the scene generators: the single persistent NetworkManager
    /// prefab, the Player prefab (free-roam + combat components on one NetworkObject), a reusable
    /// white sprite for placeholder art, and small wiring utilities. Keeps the per-scene builders
    /// thin and consistent.
    /// </summary>
    public static class SceneBuildCommon
    {
        public const string PrefabDir = "Assets/2CT/Prefabs";
        public const string ArtDir = "Assets/2CT/Art";
        public const string SceneDir = "Assets/Scenes";   // use the project's existing Scenes folder

        // =====================================================================
        //  Prefabs
        // =====================================================================
        /// <summary>The persistent Player prefab: one NetworkObject with free-roam + combat behaviours.</summary>
        public static GameObject BuildPlayerPrefab()
        {
            EnsureFolder(PrefabDir);
            string path = PrefabDir + "/Player.prefab";
            var go = new GameObject("Player");
            go.AddComponent<SpriteRenderer>();   // sprite is assigned at runtime by PlayerAvatar/FreeRoamPlayer
            go.AddComponent<NetworkObject>();
            go.AddComponent<PlayerCombatant>();
            go.AddComponent<PlayerAvatar>();
            go.AddComponent<FreeRoamPlayer>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path); // same path => stable GUID
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>The persistent NetworkManager prefab (NetworkManager + transport + ConnectionManager).</summary>
        public static GameObject BuildNetworkManagerPrefab(GameObject playerPrefab)
        {
            EnsureFolder(PrefabDir);
            string path = PrefabDir + "/NetworkManager.prefab";
            var go = new GameObject("NetworkManager");
            var nm = go.AddComponent<NetworkManager>();
            var utp = go.AddComponent<UnityTransport>();
            go.AddComponent<ConnectionManager>();
            ConfigureNetworkManager(nm, utp, playerPrefab);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>The editable Card prefab used by HandView (built via CardView.EnsureVisuals).</summary>
        public static CardView BuildCardPrefab()
        {
            EnsureFolder(PrefabDir);
            string path = PrefabDir + "/Card.prefab";
            var go = new GameObject("Card", typeof(RectTransform));
            var cv = go.AddComponent<CardView>();
            cv.EnsureVisuals();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<CardView>();
        }

        /// <summary>Adds a NetworkBootstrap object that spawns the shared NetworkManager on scene load.</summary>
        public static void AddNetworkBootstrap(GameObject nmPrefab)
        {
            var go = new GameObject("NetworkBootstrap");
            var boot = go.AddComponent<NetworkBootstrap>();
            SetPrivate(boot, "networkManagerPrefab", nmPrefab);
        }

        private static void ConfigureNetworkManager(NetworkManager nm, UnityTransport utp, GameObject playerPrefab)
        {
            var so = new SerializedObject(nm);
            var config = so.FindProperty("NetworkConfig");
            if (config == null) { Debug.LogWarning("[2CT] NetworkConfig not found — assign Player Prefab & Transport manually."); return; }
            SetRelative(config, "PlayerPrefab", playerPrefab);
            SetRelative(config, "NetworkTransport", utp);
            var approval = config.FindPropertyRelative("ConnectionApproval");
            if (approval != null) approval.boolValue = true;
            so.ApplyModifiedProperties();
        }

        private static void SetRelative(SerializedProperty parent, string name, Object value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.objectReferenceValue = value;
            else Debug.LogWarning($"[2CT] NetworkConfig.{name} not found (Netcode version difference) — set it manually.");
        }

        // =====================================================================
        //  Placeholder art
        // =====================================================================
        /// <summary>A reusable 8×8 white sprite asset (tinted per use) for backgrounds/props.</summary>
        public static Sprite EnsureWhiteSprite()
        {
            EnsureFolder(ArtDir);
            string path = ArtDir + "/White.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var px = new Color32[64];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 8;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        public static SpriteRenderer NewSprite(string name, Vector3 pos, Vector3 scale, Color color, int sortingOrder, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.color = color; sr.sortingOrder = sortingOrder;
            return sr;
        }

        // =====================================================================
        //  Utilities
        // =====================================================================
        public static void SetPrivate(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p != null) { p.objectReferenceValue = value; so.ApplyModifiedProperties(); }
            else Debug.LogWarning($"[2CT] Field '{field}' not found on {target.GetType().Name}.");
        }

        public static void SetPrivateList<T>(Object target, string field, IList<T> values) where T : Object
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[2CT] List field '{field}' not found on {target.GetType().Name}."); return; }
            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedProperties();
        }

        public static T FindAsset<T>(string nameNoExt) where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets($"{nameNoExt} t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == nameNoExt) return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return null;
        }

        /// <summary>
        /// Recompute GlobalObjectIdHash for every scene-placed NetworkObject. Objects created via
        /// script get hash 0 (the hash is normally computed in OnValidate against the object's
        /// saved scene fileID, which doesn't exist until the scene is saved). Colliding zero
        /// hashes make in-scene NetworkObjects fail to register/spawn, which breaks their RPCs.
        /// Call this AFTER a first SaveScene (so fileIDs exist), then save again.
        /// </summary>
        public static void RefreshSceneNetworkObjectHashes()
        {
            var objs = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
            var m = typeof(NetworkObject).GetMethod("GenerateGlobalObjectIdHash", BindingFlags.NonPublic | BindingFlags.Instance)
                 ?? typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) { Debug.LogWarning("[2CT] Could not find NetworkObject hash method; in-scene hashes may collide (check for a Netcode version change)."); return; }
            foreach (var no in objs)
            {
                m.Invoke(no, null);
                EditorUtility.SetDirty(no);
            }
        }

        public static void AddSceneToBuild(string path, bool makeFirst = false)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            var entry = new EditorBuildSettingsScene(path, true);
            if (makeFirst) scenes.Insert(0, entry); else scenes.Add(entry);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        public static void EnsureFolder(string folder)
        {
            // Physical-filesystem check: AssetDatabase.IsValidFolder is unreliable inside a
            // StartAssetEditing() batch (unregistered new folders → duplicate "Name 1" folders).
            if (Directory.Exists(folder)) return;
            var parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!Directory.Exists(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
