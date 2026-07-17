using TwoCT.Data;
using TwoCT.FreeRoam;
using UnityEditor;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Interactable inspector. Draws the plain settings, then a per-line dialogue editor where each
    /// line gets a typed "Add Action" dropdown (swap a sprite, remove a wall, …) via the shared
    /// <see cref="SerializeReferenceListEditor"/>. New DialogueAction subclasses appear automatically.
    /// </summary>
    [CustomEditor(typeof(Interactable))]
    public class InteractableEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "lines", "m_Script");
            EditorGUILayout.Space(8);

            var linesProp = serializedObject.FindProperty("lines");
            EditorGUILayout.LabelField("Dialogue Lines", EditorStyles.boldLabel);

            // Structural edits are deferred until after the layout groups close, so we never break
            // out of a BeginVertical/BeginHorizontal mid-iteration (which unbalances IMGUI layout).
            int deleteAt = -1, moveFrom = -1, moveTo = -1;

            for (int i = 0; i < linesProp.arraySize; i++)
            {
                var line = linesProp.GetArrayElementAtIndex(i);
                var speaker = line.FindPropertyRelative("speaker");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                string title = string.IsNullOrEmpty(speaker.stringValue) ? "(no speaker)" : speaker.stringValue;
                line.isExpanded = EditorGUILayout.Foldout(line.isExpanded, $"Line {i}:  {title}", true);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("▲", GUILayout.Width(24))) { moveFrom = i; moveTo = i - 1; }
                using (new EditorGUI.DisabledScope(i == linesProp.arraySize - 1))
                    if (GUILayout.Button("▼", GUILayout.Width(24))) { moveFrom = i; moveTo = i + 1; }
                if (GUILayout.Button("✕", GUILayout.Width(24))) deleteAt = i;
                EditorGUILayout.EndHorizontal();

                if (line.isExpanded && deleteAt != i)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(speaker);
                    EditorGUILayout.PropertyField(line.FindPropertyRelative("text"));
                    EditorGUILayout.PropertyField(line.FindPropertyRelative("autoAdvanceSeconds"));
                    EditorGUILayout.Space(2);
                    // Draw() applies its own changes; caller must Update() first (done above) and not re-Update().
                    SerializeReferenceListEditor.Draw(
                        line.FindPropertyRelative("actions"), typeof(DialogueAction), "＋ Add Action");
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("＋ Add Line"))
            {
                int idx = linesProp.arraySize;
                linesProp.arraySize++;
                var added = linesProp.GetArrayElementAtIndex(idx);
                added.isExpanded = true;
                added.FindPropertyRelative("speaker").stringValue = "";
                added.FindPropertyRelative("text").stringValue = "";
                added.FindPropertyRelative("autoAdvanceSeconds").floatValue = 2.5f;
                added.FindPropertyRelative("actions").arraySize = 0;
            }

            if (deleteAt >= 0) linesProp.DeleteArrayElementAtIndex(deleteAt);
            else if (moveFrom >= 0) linesProp.MoveArrayElement(moveFrom, moveTo);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
