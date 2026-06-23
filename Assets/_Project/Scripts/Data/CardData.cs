using UnityEngine;

[CreateAssetMenu(fileName = "NewCard", menuName = "Chess/Card Data")]
public class CardData : ScriptableObject
{
    [Header("Basic Info")]
    public string cardName;
    public CardEffectType effectType;

    [Tooltip("Để trống nếu áp dụng cho mọi quân. Nếu chỉ dùng cho Vua, gõ 'King'")]
    public string requiredTargetName = "";

    [Header("Balancing Settings")]
    public int baseCooldown = 0;
    public int maxUses = 1;

    public int effectValue1;       // VD: Tầm lướt của KingDash, lượng Dame buff thêm...
    public int effectValue2;       // VD: Thời gian cấm đánh của BishopSilence, lượng HP buff...
}