using TwoCT.FreeRoam;
using UnityEditor;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Scene-view editor for <see cref="FreeRoamWall"/>: drag the edge and corner handles to
    /// resize the collision box, the way Unity's own "Edit Collider" mode works — no more
    /// typing numbers into Size on an empty GameObject.
    ///
    /// Dragging an edge keeps the opposite edge pinned and moves the transform so the box stays
    /// glued to where you dropped it. Hold Alt to resize symmetrically about the centre instead.
    /// </summary>
    [CustomEditor(typeof(FreeRoamWall))]
    [CanEditMultipleObjects]
    public class FreeRoamWallEditor : Editor
    {
        private const float MinSize = 0.05f;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Drag the square handles in the Scene view to resize this wall.\n" +
                "Hold Alt to resize symmetrically about the centre.",
                MessageType.Info);
        }

        private void OnSceneGUI()
        {
            var wall = (FreeRoamWall)target;
            Vector2 center = wall.transform.position;
            Vector2 half = wall.size * 0.5f;
            bool symmetric = Event.current.alt;

            // Filled + outlined box so it reads clearly while editing.
            Handles.color = new Color(1f, 0.4f, 0.2f, 0.9f);
            Vector3[] corners =
            {
                new Vector3(center.x - half.x, center.y - half.y),
                new Vector3(center.x - half.x, center.y + half.y),
                new Vector3(center.x + half.x, center.y + half.y),
                new Vector3(center.x + half.x, center.y - half.y),
            };
            Handles.DrawSolidRectangleWithOutline(corners, new Color(1f, 0.4f, 0.2f, 0.08f), new Color(1f, 0.4f, 0.2f, 0.9f));

            EditorGUI.BeginChangeCheck();

            float left = center.x - half.x;
            float right = center.x + half.x;
            float bottom = center.y - half.y;
            float top = center.y + half.y;

            // Edge handles: each slides along one axis.
            right  = EdgeHandle(new Vector2(right, center.y),  Vector3.right, right);
            left   = EdgeHandle(new Vector2(left, center.y),   Vector3.right, left);
            top    = EdgeHandle(new Vector2(center.x, top),    Vector3.up,    top);
            bottom = EdgeHandle(new Vector2(center.x, bottom), Vector3.up,    bottom);

            // Corner handles: move freely in the plane, driving one x-edge and one y-edge.
            CornerHandle(ref right, ref top,    new Vector2(right, top));
            CornerHandle(ref left,  ref top,    new Vector2(left, top));
            CornerHandle(ref right, ref bottom, new Vector2(right, bottom));
            CornerHandle(ref left,  ref bottom, new Vector2(left, bottom));

            if (EditorGUI.EndChangeCheck())
            {
                Vector2 newSize, newCenter;
                if (symmetric)
                {
                    // Mirror whichever edges moved around the fixed centre.
                    float hx = Mathf.Max(Mathf.Abs(right - center.x), Mathf.Abs(center.x - left), MinSize * 0.5f);
                    float hy = Mathf.Max(Mathf.Abs(top - center.y), Mathf.Abs(center.y - bottom), MinSize * 0.5f);
                    newSize = new Vector2(hx * 2f, hy * 2f);
                    newCenter = center;
                }
                else
                {
                    newSize = new Vector2(Mathf.Max(right - left, MinSize), Mathf.Max(top - bottom, MinSize));
                    newCenter = new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
                }

                Undo.RecordObject(wall.transform, "Resize Wall");
                Undo.RecordObject(wall, "Resize Wall");
                wall.transform.position = new Vector3(newCenter.x, newCenter.y, wall.transform.position.z);
                wall.size = newSize;
                EditorUtility.SetDirty(wall);
            }
        }

        /// <summary>Draggable square that slides along <paramref name="axis"/>; returns the moved coordinate.</summary>
        private static float EdgeHandle(Vector2 pos, Vector3 axis, float current)
        {
            float sizeH = HandleUtility.GetHandleSize(pos) * 0.08f;
            Vector3 moved = Handles.Slider(pos, axis, sizeH, Handles.DotHandleCap, 0f);
            return axis == Vector3.right ? moved.x : moved.y;
        }

        /// <summary>Draggable square that moves in the XY plane, driving one x-edge and one y-edge.</summary>
        private static void CornerHandle(ref float x, ref float y, Vector2 pos)
        {
            float sizeH = HandleUtility.GetHandleSize(pos) * 0.07f;
            Vector3 moved = Handles.FreeMoveHandle(pos, sizeH, Vector3.zero, Handles.RectangleHandleCap);
            x = moved.x;
            y = moved.y;
        }
    }
}
