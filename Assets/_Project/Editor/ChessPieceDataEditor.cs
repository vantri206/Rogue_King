using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(ChessPieceData))]
public class ChessPieceDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        ChessPieceData data = (ChessPieceData)target;

        DrawDefaultInspector();

        GUILayout.Space(15);
        GUILayout.Label("Movement Pattern (5x5 Grid)", EditorStyles.boldLabel);
        GUILayout.Label("Click to toggle move directions. Center is the piece.", EditorStyles.helpBox);

        bool gridChanged = false;

        GUILayout.BeginVertical();
        for (int y = 0; y < 5; y++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            for (int x = 0; x < 5; x++)
            {
                int index = y * 5 + x;
                bool isCenter = (x == 2 && y == 2);

                if (isCenter)
                {
                    GUILayout.Button("P", GUILayout.Width(30), GUILayout.Height(30));
                }
                else
                {
                    bool currentState = data.movePatternGrid[index];

                    GUI.backgroundColor = currentState ? Color.green : Color.white;

                    bool newState = GUILayout.Toggle(currentState, "", "Button", GUILayout.Width(30), GUILayout.Height(30));

                    GUI.backgroundColor = Color.white;

                    if (newState != currentState)
                    {
                        data.movePatternGrid[index] = newState;
                        gridChanged = true;
                    }
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();

        if (gridChanged)
        {
            BakeDirections(data);
            EditorUtility.SetDirty(data);
        }
    }

    private void BakeDirections(ChessPieceData data)
    {
        data.moveDirections.Clear();

        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                int index = y * 5 + x;
                if (data.movePatternGrid[index])
                {
                    int vectorX = x - 2;
                    int vectorY = 2 - y;

                    data.moveDirections.Add(new Vector2Int(vectorX, vectorY));
                }
            }
        }
    }
}