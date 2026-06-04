using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(LevelData))]
public class LevelDataEditor : Editor
{
    private int currentTab = 0;
    private Vector2Int selectedTile = new Vector2Int(-1, -1);

    private readonly string[] tabLabels = { "1. Edit Map", "2. Edit Start Pieces" };

    public override void OnInspectorGUI()
    {
        LevelData data = (LevelData)target;

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("boardWidth"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("boardHeight"));

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
        }

        GUILayout.Space(15);

        currentTab = GUILayout.Toolbar(currentTab, tabLabels, GUILayout.Height(30));
        GUILayout.Space(10);

        if (currentTab == 0)
        {
            DrawShapeEditor(data);
        }
        else
        {
            DrawPieceEditor(data);
        }
    }

    private void DrawShapeEditor(LevelData data)
    {
        GUILayout.Label("Map Layout Design", EditorStyles.boldLabel);
        GUILayout.Label("Green = Tile Exists | Red = Hole / Null", EditorStyles.helpBox);

        bool hasChanged = false;

        GUILayout.BeginVertical();
        for (int y = data.boardHeight - 1; y >= 0; y--)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            for (int x = 0; x < data.boardWidth; x++)
            {
                int index = y * data.boardWidth + x;
                bool tileExists = data.tileExistenceMap[index];

                GUI.backgroundColor = tileExists ? Color.green : Color.red;
                string label = tileExists ? "■" : " ";

                bool newState = GUILayout.Toggle(tileExists, label, "Button", GUILayout.Width(25), GUILayout.Height(25));
                GUI.backgroundColor = Color.white;

                if (newState != tileExists)
                {
                    data.tileExistenceMap[index] = newState;

                    if (!newState)
                    {
                        data.initialPieces.RemoveAll(p => p.startPosition == new Vector2Int(x, y));
                    }
                    hasChanged = true;
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();

        GUILayout.Space(10);
        if (GUILayout.Button("Fill Entire Board (Reset)", GUILayout.Height(25)))
        {
            for (int i = 0; i < data.tileExistenceMap.Length; i++)
                data.tileExistenceMap[i] = true;
            hasChanged = true;
        }

        if (hasChanged) EditorUtility.SetDirty(data);
    }
    private void DrawPieceEditor(LevelData data)
    {
        GUILayout.Label("Piece Placement", EditorStyles.boldLabel);
        GUILayout.Label("Click a valid tile to place or edit a chess piece.", EditorStyles.helpBox);

        bool hasChanged = false;

        GUILayout.BeginVertical();
        for (int y = data.boardHeight - 1; y >= 0; y--)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            for (int x = 0; x < data.boardWidth; x++)
            {
                int index = y * data.boardWidth + x;
                bool tileExists = data.tileExistenceMap[index];
                Vector2Int pos = new Vector2Int(x, y);

                if (!tileExists)
                {
                    GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                    GUILayout.Box("", GUILayout.Width(35), GUILayout.Height(35));
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    int pieceIndex = data.initialPieces.FindIndex(p => p.startPosition == pos);
                    bool hasPiece = (pieceIndex >= 0);
                    bool isSelected = (selectedTile == pos);

                    string tileLabel = "-";
                    if (hasPiece)
                    {
                        InitialPieceSetup setup = data.initialPieces[pieceIndex];

                        if (setup.faction == ChessFaction.ChessAlliance)
                            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f); 
                        else
                            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);

                        if (setup.pieceData != null && !string.IsNullOrEmpty(setup.pieceData.pieceName))
                        {
                            string pName = setup.pieceData.pieceName;
                            tileLabel = pName.Length <= 3 ? pName : pName.Substring(0, 3);
                        }
                        else
                        {
                            tileLabel = "???";
                        }
                    }
                    else
                    {
                        GUI.backgroundColor = Color.white;
                    }

                    if (isSelected) GUI.backgroundColor = Color.yellow;

                    if (GUILayout.Button(tileLabel, GUILayout.Width(35), GUILayout.Height(35)))
                    {
                        selectedTile = pos;
                        GUI.FocusControl(null);
                    }

                    GUI.backgroundColor = Color.white;
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();

        GUILayout.Space(15);
        DrawUILine(Color.gray);
        GUILayout.Space(10);

        if (selectedTile.x >= 0 && selectedTile.x < data.boardWidth &&
            selectedTile.y >= 0 && selectedTile.y < data.boardHeight)
        {
            int index = selectedTile.y * data.boardWidth + selectedTile.x;

            if (!data.tileExistenceMap[index])
            {
                GUILayout.Label("This tile is a hole. You cannot place a piece here.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                GUILayout.Label($"Editing Tile: ({selectedTile.x}, {selectedTile.y})", EditorStyles.boldLabel);

                int pieceIndex = data.initialPieces.FindIndex(p => p.startPosition == selectedTile);

                if (pieceIndex >= 0)
                {
                    InitialPieceSetup setup = data.initialPieces[pieceIndex];

                    EditorGUI.BeginChangeCheck();

                    setup.pieceData = (ChessPieceData)EditorGUILayout.ObjectField("Piece Data", setup.pieceData, typeof(ChessPieceData), false);
                    setup.faction = (ChessFaction)EditorGUILayout.EnumPopup("Faction", setup.faction);

                    if (EditorGUI.EndChangeCheck())
                    {
                        data.initialPieces[pieceIndex] = setup;
                        hasChanged = true;
                    }

                    GUILayout.Space(10);
                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("Remove Piece", GUILayout.Height(30)))
                    {
                        data.initialPieces.RemoveAt(pieceIndex);
                        hasChanged = true;
                    }
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    GUILayout.Space(10);
                    if (GUILayout.Button("Add New Piece Here", GUILayout.Height(30)))
                    {
                        data.initialPieces.Add(new InitialPieceSetup
                        {
                            startPosition = selectedTile,
                            faction = ChessFaction.ChessAlliance
                        });
                        hasChanged = true;
                    }
                }
            }
        }
        else
        {
            GUILayout.Label("Select a tile above to set up a piece.", EditorStyles.centeredGreyMiniLabel);
        }

        if (hasChanged)
        {
            EditorUtility.SetDirty(data);
        }
    }
    private void DrawUILine(Color color, int thickness = 1, int padding = 10)
    {
        Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(padding + thickness));
        r.height = thickness;
        r.y += padding / 2;
        r.x -= 2;
        r.width += 6;
        EditorGUI.DrawRect(r, color);
    }
}