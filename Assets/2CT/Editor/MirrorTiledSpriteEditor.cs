using TwoCT.FreeRoam;
using UnityEditor;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Scene-view editor for <see cref="MirrorTiledSprite"/>: drag the edge/corner handles to stretch
    /// the tiled span left/right and up/down, watching the mirrored copies fill in live. Mirrors the
    /// feel of FreeRoamWall's editor. Hold Alt to stretch symmetrically about the centre.
    /// </summary>
    [CustomEditor(typeof(MirrorTiledSprite))]
    [CanEditMultipleObjects]
    public class MirrorTiledSpriteEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Drag the square handles in the Scene view to stretch the tiled span.\n" +
                "Neighbours mirror-flip so edges stay seamless. Hold Alt to stretch about the centre.",
                MessageType.Info);
        }

        private void OnSceneGUI()
        {
            var t = (MirrorTiledSprite)target;
            var sr = t.GetComponent<SpriteRenderer>();
            Vector2 spriteSize = sr != null && sr.sprite != null ? (Vector2)sr.sprite.bounds.size : Vector2.one;

            Vector2 center = t.transform.position;
            // Handles act on the visible extent (at least one tile), so a zero span still shows grips.
            Vector2 half = new Vector2(Mathf.Max(t.span.x, spriteSize.x), Mathf.Max(t.span.y, spriteSize.y)) * 0.5f;
            bool symmetric = Event.current.alt;

            Vector3[] corners =
            {
                new Vector3(center.x - half.x, center.y - half.y),
                new Vector3(center.x - half.x, center.y + half.y),
                new Vector3(center.x + half.x, center.y + half.y),
                new Vector3(center.x + half.x, center.y - half.y),
            };
            Handles.DrawSolidRectangleWithOutline(corners, new Color(0.5f, 0.9f, 1f, 0.06f), new Color(0.5f, 0.9f, 1f, 0.9f));

            EditorGUI.BeginChangeCheck();

            float left = center.x - half.x;
            float right = center.x + half.x;
            float bottom = center.y - half.y;
            float top = center.y + half.y;

            right  = EdgeHandle(new Vector2(right, center.y),  Vector3.right, right);
            left   = EdgeHandle(new Vector2(left, center.y),   Vector3.right, left);
            top    = EdgeHandle(new Vector2(center.x, top),    Vector3.up,    top);
            bottom = EdgeHandle(new Vector2(center.x, bottom), Vector3.up,    bottom);

            CornerHandle(ref right, ref top,    new Vector2(right, top));
            CornerHandle(ref left,  ref top,    new Vector2(left, top));
            CornerHandle(ref right, ref bottom, new Vector2(right, bottom));
            CornerHandle(ref left,  ref bottom, new Vector2(left, bottom));

            if (EditorGUI.EndChangeCheck())
            {
                Vector2 newSpan, newCenter;
                if (symmetric)
                {
                    float hx = Mathf.Max(Mathf.Abs(right - center.x), Mathf.Abs(center.x - left), spriteSize.x * 0.5f);
                    float hy = Mathf.Max(Mathf.Abs(top - center.y), Mathf.Abs(center.y - bottom), spriteSize.y * 0.5f);
                    newSpan = new Vector2(hx * 2f, hy * 2f);
                    newCenter = center;
                }
                else
                {
                    newSpan = new Vector2(Mathf.Max(right - left, spriteSize.x), Mathf.Max(top - bottom, spriteSize.y));
                    newCenter = new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
                }

                Undo.RecordObject(t.transform, "Stretch Mirror Tiles");
                Undo.RecordObject(t, "Stretch Mirror Tiles");
                t.transform.position = new Vector3(newCenter.x, newCenter.y, t.transform.position.z);
                t.span = newSpan;
                EditorUtility.SetDirty(t);
                t.Rebuild();
            }
        }

        private static float EdgeHandle(Vector2 pos, Vector3 axis, float current)
        {
            float sizeH = HandleUtility.GetHandleSize(pos) * 0.08f;
            Vector3 moved = Handles.Slider(pos, axis, sizeH, Handles.DotHandleCap, 0f);
            return axis == Vector3.right ? moved.x : moved.y;
        }

        private static void CornerHandle(ref float x, ref float y, Vector2 pos)
        {
            float sizeH = HandleUtility.GetHandleSize(pos) * 0.07f;
            Vector3 moved = Handles.FreeMoveHandle(pos, sizeH, Vector3.zero, Handles.RectangleHandleCap);
            x = moved.x;
            y = moved.y;
        }
    }
}
