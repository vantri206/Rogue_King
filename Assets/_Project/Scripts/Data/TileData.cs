public class TileData
{
    public int gridX { get; private set; }
    public int gridY { get; private set; }
    public string currentPieceID { get; set; }

    public TileData(int x, int y)
    {
        gridX = x;
        gridY = y;
        currentPieceID = null;
    }
}