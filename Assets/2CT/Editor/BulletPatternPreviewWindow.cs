using System.Collections.Generic;
using TwoCT.Core;
using TwoCT.Data;
using UnityEditor;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Animated preview of a BulletPatternSO without entering Play mode. Pick a pattern, scrub or
    /// play the timeline, and reseed to sanity-check the random spread. Mirrors the runtime layout
    /// (boss muzzle on the right firing toward the arena box on the left).
    /// </summary>
    public class BulletPatternPreviewWindow : EditorWindow
    {
        private BulletPatternSO _pattern;
        private int _seed = 12345;
        private float _time;
        private bool _playing;
        private double _lastUpdate;
        private List<BulletSpawnData> _schedule;

        // World-space layout matching the built scene.
        private static readonly Vector2 Muzzle = new Vector2(5f, 0f);
        private static readonly Vector2 ArenaCenter = new Vector2(-0.5f, -0.5f);
        private static readonly Vector2 ArenaSize = new Vector2(6f, 3.5f);

        [MenuItem("2CT/Bullet Pattern Preview", priority = 41)]
        public static void Open() => GetWindow<BulletPatternPreviewWindow>("Bullet Preview");

        private void OnEnable() { EditorApplication.update += Tick; _lastUpdate = EditorApplication.timeSinceStartup; }
        private void OnDisable() { EditorApplication.update -= Tick; }

        private void Tick()
        {
            if (!_playing || _pattern == null) return;
            double now = EditorApplication.timeSinceStartup;
            _time += (float)(now - _lastUpdate);
            _lastUpdate = now;
            if (_time > _pattern.duration) _time = 0f;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var newPattern = (BulletPatternSO)EditorGUILayout.ObjectField(_pattern, typeof(BulletPatternSO), false);
            if (newPattern != _pattern) { _pattern = newPattern; Rebuild(); }
            if (GUILayout.Button(_playing ? "Pause" : "Play", EditorStyles.toolbarButton, GUILayout.Width(60)))
            { _playing = !_playing; _lastUpdate = EditorApplication.timeSinceStartup; }
            if (GUILayout.Button("Restart", EditorStyles.toolbarButton, GUILayout.Width(60))) _time = 0f;
            GUILayout.Label("Seed", GUILayout.Width(34));
            int newSeed = EditorGUILayout.IntField(_seed, GUILayout.Width(80));
            if (newSeed != _seed) { _seed = newSeed; Rebuild(); }
            if (GUILayout.Button("Reseed", EditorStyles.toolbarButton, GUILayout.Width(60))) { _seed = Random.Range(0, int.MaxValue); Rebuild(); }
            EditorGUILayout.EndHorizontal();

            if (_pattern == null) { EditorGUILayout.HelpBox("Select a Bullet Pattern to preview.", MessageType.Info); return; }
            if (_schedule == null) Rebuild();

            _time = EditorGUILayout.Slider(_time, 0f, _pattern.duration);
            EditorGUILayout.LabelField(_pattern.Summary, EditorStyles.miniLabel);

            var view = GUILayoutUtility.GetRect(position.width, position.height - 70);
            DrawPreview(view);
        }

        private void Rebuild()
        {
            if (_pattern == null) { _schedule = null; _time = 0f; return; }
            Vector2 aim = ArenaCenter - Muzzle;
            var ctx = new PatternContext
            {
                muzzle = Muzzle,
                center = ArenaCenter,
                aim = aim.sqrMagnitude < 0.0001f ? Vector2.left : aim.normalized,
                arenaBounds = new Rect(ArenaCenter - ArenaSize * 0.5f, ArenaSize),
                arenaInner = new Rect(ArenaCenter - (ArenaSize - Vector2.one * 0.4f) * 0.5f, ArenaSize - Vector2.one * 0.4f),
            };
            _schedule = _pattern.BuildSchedule(_seed, ctx);
            _time = 0f;
        }

        private void DrawPreview(Rect view)
        {
            EditorGUI.DrawRect(view, new Color(0.09f, 0.09f, 0.13f));

            // World -> view mapping.
            const float worldHalf = 8f;
            Vector2 Map(Vector2 world)
            {
                float nx = (world.x + worldHalf) / (worldHalf * 2f);
                float ny = (world.y + worldHalf) / (worldHalf * 2f);
                return new Vector2(view.x + nx * view.width, view.y + (1f - ny) * view.height);
            }

            // Arena box.
            Vector2 aMin = Map(ArenaCenter + new Vector2(-ArenaSize.x, ArenaSize.y) * 0.5f);
            Vector2 aMax = Map(ArenaCenter + new Vector2(ArenaSize.x, -ArenaSize.y) * 0.5f);
            EditorGUI.DrawRect(new Rect(aMin.x, aMin.y, aMax.x - aMin.x, aMax.y - aMin.y), new Color(0.2f, 0.2f, 0.3f, 0.5f));

            // Muzzle.
            var m = Map(Muzzle);
            EditorGUI.DrawRect(new Rect(m.x - 6, m.y - 6, 12, 12), new Color(0.8f, 0.3f, 0.3f));

            // Live bullets.
            int alive = 0;
            if (_schedule != null)
                foreach (var b in _schedule)
                {
                    if (b.time > _time) continue;
                    float age = _time - b.time;
                    if (age > b.lifetime) continue;
                    Vector2 world = Muzzle + b.originOffset + b.velocity * age;
                    var p = Map(world);
                    float s = Mathf.Max(3f, b.radius * 2f * (view.width / (worldHalf * 2f)));
                    EditorGUI.DrawRect(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), b.color);
                    alive++;
                }

            GUI.Label(new Rect(view.x + 6, view.y + 4, 200, 20), $"t={_time:0.0}s  bullets={alive}");
        }
    }
}
