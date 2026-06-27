using UnityEngine;

[CreateAssetMenu(fileName = "NewCard", menuName = "Chess/Card Data")]
public class CardData : ScriptableObject
{
    [Header("Basic Info")]
    public string cardName;

    public Sprite cardArtwork;

    [Header("Card Role")]
    [Tooltip("RogueKing card chỉ được đưa vào deck/và sử dụng khi player đang cầm Rogue King. ChessAlliance card chỉ dùng khi player đang cầm phe Chess Alliance.")]
    public CardRoleType cardRole = CardRoleType.RogueKing;

    public CardEffectType effectType;

    [Tooltip("Để trống nếu card không cần target. Nếu card cần target là Vua, gõ 'King'.")]
    public string requiredTargetName = "";

    [Header("Balancing Settings")]
    [Min(0)] public int baseCooldown = 0;
    [Min(1)] public int maxUses = 1;

    public int effectValue1;
    public int effectValue2;

    [Header("Summon Captured Pawn Settings")]
    [Tooltip("Dùng cho CardEffectType.SummonCapturedPawn. Kéo ChessPieceData của quân Tốt phe Rogue/màu xanh vào đây.")]
    public ChessPieceData summonPieceData;
}
