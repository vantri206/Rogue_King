using UnityEngine;

[CreateAssetMenu(fileName = "NewCard", menuName = "Chess/Card Data")]
public class CardData : ScriptableObject
{
    [Header("Basic Info")]
    public string cardName;
    public CardEffectType effectType; // Thêm loại hiệu ứng

    [Tooltip("Để trống nếu card không cần target. Nếu card cần target là Vua, gõ 'King'.")]
    public string requiredTargetName = "";

    [Header("Balancing Settings")]
    [Min(0)] public int baseCooldown = 0;
    [Min(1)] public int maxUses = 1;

    // Thêm các biến giá trị để tùy chỉnh sức mạnh trên Inspector
    public int effectValue1;
    public int effectValue2;
}