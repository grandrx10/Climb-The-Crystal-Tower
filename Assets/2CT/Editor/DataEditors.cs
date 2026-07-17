using TwoCT.Core;
using TwoCT.Data;
using UnityEditor;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>Card inspector with the typed effect dropdown + auto rules-text preview.</summary>
    [CustomEditor(typeof(CardData))]
    public class CardDataEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "effects", "m_Script");
            EditorGUILayout.Space(6);
            SerializeReferenceListEditor.Draw(serializedObject.FindProperty("effects"), typeof(CardEffect), "＋ Add Effect");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8);
            var card = (CardData)target;
            EditorGUILayout.HelpBox($"Preview:  [{card.manaCost} mana]  {card.RulesText}", MessageType.None);
        }
    }

    /// <summary>Mythical inspector — same effect dropdown.</summary>
    [CustomEditor(typeof(MythicalData))]
    public class MythicalDataEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "effects", "m_Script");
            EditorGUILayout.Space(6);
            SerializeReferenceListEditor.Draw(serializedObject.FindProperty("effects"), typeof(CardEffect), "＋ Add Effect");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8);
            var m = (MythicalData)target;
            string cost = m.kind == MythicalKind.Active ? $"[{m.manaCost} mana] " : "[Passive] ";
            EditorGUILayout.HelpBox($"Preview:  {cost}{m.RulesText}", MessageType.None);
        }
    }

    /// <summary>Bullet-pattern inspector with the typed emitter dropdown + summary.</summary>
    [CustomEditor(typeof(BulletPatternSO))]
    public class BulletPatternEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "emitters", "m_Script");
            EditorGUILayout.Space(6);
            SerializeReferenceListEditor.Draw(serializedObject.FindProperty("emitters"), typeof(BulletEmitter), "＋ Add Emitter");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8);
            var p = (BulletPatternSO)target;
            EditorGUILayout.HelpBox(p.Summary, MessageType.None);
            EditorGUILayout.LabelField("Open the Bullet Pattern Preview window to visualise the schedule.", EditorStyles.miniLabel);
        }
    }
}
