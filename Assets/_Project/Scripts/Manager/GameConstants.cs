
public enum GameState
{
    Setup,             
    PlayerTurn,         
    ResolvingAction,    
    PhaseTransition,
    GameOver          
}

public enum NetGameState 
{ 
    Init, 
    Setup, 
    KingTurn, 
    ChessTurn, 
    ResolvingAction, 
    PhaseTransition, 
    GameOver 
}

public enum GamePhase
{
    Phase1,
    Phase2
}

public enum ChessFaction
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
public enum SkillType
{
    None,
    PawnShield,     
    BishopSilence,
    KingRevive,   
    QueenGuard,
    KingSweep,
    KingDash,  
    KingCharm
}