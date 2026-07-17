using System;
using UnityEditor;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Reusable inspector drawer for a <c>[SerializeReference]</c> list of a polymorphic base
    /// type. Gives designers a typed "Add ▾" dropdown (every non-abstract subclass appears
    /// automatically) plus per-element remove/reorder — the missing UX for managed-reference
    /// lists. Used for card/mythical effects and bullet emitters.
    /// </summary>
    public static class SerializeReferenceListEditor
    {
        public static void Draw(SerializedProperty listProp, Type baseType, string addLabel)
        {
            var so = listProp.serializedObject;
            // Do NOT call so.Update() here. Callers invoke serializedObject.Update() before drawing
            // their own fields (e.g. a pattern's duration); a second Update() would discard those
            // pending edits before ApplyModifiedProperties() runs, silently reverting them.

            EditorGUILayout.LabelField(listProp.displayName, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            for (int i = 0; i < listProp.arraySize; i++)
            {
                var element = listProp.GetArrayElementAtIndex(i);

                EditorGUILayout.BeginHorizontal();
                string typeName = FriendlyTypeName(element.managedReferenceFullTypename);
                element.isExpanded = EditorGUILayout.Foldout(element.isExpanded, $"{i}:  {typeName}", true);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("▲", GUILayout.Width(24))) { listProp.MoveArrayElement(i, i - 1); break; }
                using (new EditorGUI.DisabledScope(i == listProp.arraySize - 1))
                    if (GUILayout.Button("▼", GUILayout.Width(24))) { listProp.MoveArrayElement(i, i + 1); break; }
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    listProp.DeleteArrayElementAtIndex(i);
                    so.ApplyModifiedProperties();
                    EditorGUILayout.EndHorizontal();
                    return;
                }
                EditorGUILayout.EndHorizontal();

                if (element.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(element, includeChildren: true);
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUI.indentLevel--;

            if (GUILayout.Button(addLabel))
                ShowAddMenu(listProp, baseType);

            so.ApplyModifiedProperties();
        }

        private static void ShowAddMenu(SerializedProperty listProp, Type baseType)
        {
            var menu = new GenericMenu();
            foreach (var t in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (t.IsAbstract) continue;
                var type = t;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.Name)), false, () =>
                {
                    listProp.serializedObject.Update();
                    int idx = listProp.arraySize;
                    listProp.arraySize++;
                    listProp.GetArrayElementAtIndex(idx).managedReferenceValue = Activator.CreateInstance(type);
                    listProp.serializedObject.ApplyModifiedProperties();
                });
            }
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("No types found"));
            menu.ShowAsContext();
        }

        private static string FriendlyTypeName(string managedReferenceFullTypename)
        {
            if (string.IsNullOrEmpty(managedReferenceFullTypename)) return "(null)";
            int space = managedReferenceFullTypename.LastIndexOf(' ');
            string full = space >= 0 ? managedReferenceFullTypename.Substring(space + 1) : managedReferenceFullTypename;
            int dot = full.LastIndexOf('.');
            return ObjectNames.NicifyVariableName(dot >= 0 ? full.Substring(dot + 1) : full);
        }
    }
}
