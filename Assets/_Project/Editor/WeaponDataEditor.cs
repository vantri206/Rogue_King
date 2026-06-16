using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(WeaponData))]
public class WeaponDataEditor : Editor
{
    private const int GridSize = 15;
    private const int CenterIndex = GridSize / 2;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        WeaponData weapon = (WeaponData)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Targeting Pattern (Yellow Range)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Center (Cyan) is the King's position.", MessageType.Info);
        DrawGrid(weapon.targetingPattern, Color.yellow);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Effect Pattern (Red AoE)", EditorStyles.boldLabel);

        string helpText = weapon.isOriginRelative
            ? "Cyan Tile is King."
            : "Cyan Tile is Target (click).";

        if (weapon.isDirectional) helpText += "\nAlways draw the weapon's direction UP.";
        EditorGUILayout.HelpBox(helpText, MessageType.Info);

        DrawGrid(weapon.effectPattern, Color.red);

        if (GUI.changed)
        {
            EditorUtility.SetDirty(weapon);
        }
    }

    private void DrawGrid(List<Vector2Int> patternList, Color activeColor)
    {
        for (int y = 0; y < GridSize; y++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            for (int x = 0; x < GridSize; x++)
            {
                Vector2Int offset = new Vector2Int(x - CenterIndex, CenterIndex - y);
                bool isActive = patternList.Contains(offset);

                Color oldColor = GUI.backgroundColor;
                if (isActive) GUI.backgroundColor = activeColor;
                else if (offset == Vector2Int.zero) GUI.backgroundColor = Color.cyan;

                if (GUILayout.Button("", GUILayout.Width(20), GUILayout.Height(20)))
                {
                    if (isActive) patternList.Remove(offset);
                    else patternList.Add(offset);
                }
                GUI.backgroundColor = oldColor;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }
}