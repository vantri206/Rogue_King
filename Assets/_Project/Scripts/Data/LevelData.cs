using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct InitialPieceSetup
{
    public ChessPieceData pieceData;
    public Vector2Int startPosition;
    public ChessFaction faction;
}

[CreateAssetMenu(fileName = "NewLevelData", menuName = "Chess/Level Data")]
public class LevelData : ScriptableObject
{
    [Header("Board Dimensions")]
    [Range(1, 20)] public int boardWidth = 8;
    [Range(1, 20)] public int boardHeight = 8;

    [HideInInspector]
    public bool[] tileExistenceMap = new bool[64];

    [HideInInspector]
    public int lastWidth = 8;
    [HideInInspector]
    public int lastHeight = 8;

    [Header("Initial Pieces")]
    [HideInInspector]
    public List<InitialPieceSetup> initialPieces = new List<InitialPieceSetup>();

    private void Reset()
    {
        lastWidth = boardWidth;
        lastHeight = boardHeight;
        FillWithTrue(boardWidth * boardHeight);
        initialPieces.Clear();
    }

    private void OnValidate()
    {
        if (boardWidth != lastWidth || boardHeight != lastHeight)
        {
            bool[] newMap = new bool[boardWidth * boardHeight];

            for (int i = 0; i < newMap.Length; i++)
            {
                newMap[i] = true;
            }

            for (int y = 0; y < Mathf.Min(boardHeight, lastHeight); y++)
            {
                for (int x = 0; x < Mathf.Min(boardWidth, lastWidth); x++)
                {
                    newMap[y * boardWidth + x] = tileExistenceMap[y * lastWidth + x];
                }
            }

            tileExistenceMap = newMap;
            lastWidth = boardWidth;
            lastHeight = boardHeight;

            initialPieces.RemoveAll(p => p.startPosition.x >= boardWidth || p.startPosition.y >= boardHeight);
        }
    }

    private void FillWithTrue(int size)
    {
        tileExistenceMap = new bool[size];
        for (int i = 0; i < tileExistenceMap.Length; i++)
        {
            tileExistenceMap[i] = true;
        }
    }
}