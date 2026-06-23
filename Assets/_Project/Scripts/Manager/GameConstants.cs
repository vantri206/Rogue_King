
public enum GameState
{
    Setup,             
    PlayerTurn,         
    ResolvingAction,    
    PhaseTransition,
    GameOver          
}

public enum GamePhase
{
    Phase1,
    Phase2
}

public enum  ChessFaction
{
    ChessRogue,     // Player 1
    ChessAlliance,  // Player 2
    Neutral         // Environment or non-aligned pieces
}

public enum MovementType
{
    Slide,  // Can move in a direction until blocked
    Jump    // Can move to specific positions regardless of obstacles
}
public enum CardEffectType
{
    None,
    PawnShield,         // Khiên Tốt
    BishopSilence,      // Phán Xét (Cấm skill)
    KingRevive,         // Hồi Sinh
    KingSweep,          // Càn Quét
    KingDash,           // Lướt Nhanh
    SuperBuff,          // Siêu Buff
    ExtraTurn,          // Bứt Tốc (Thêm Lượt)
    March,              // Hành Quân
    PawnForwardAttack,  // Tốt Ăn Thẳng
    Recall              // Đánh Úp (Giật ngược)
}