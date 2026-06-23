using Fusion;
using UnityEngine;
using System.Collections.Generic;

public class ServerCardManager : NetworkBehaviour
{
    public static ServerCardManager Instance { get; private set; }

    [Header("Global Card Database")]
    [Tooltip("Kéo thả TOÀN BỘ CardData có trong game vào đây để Server làm từ điển đối chiếu")]
    [SerializeField] private List<CardData> availableCards;

    public override void Spawned()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Runner.Despawn(Object);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
            Instance = null;
    }

    public CardData GetCardData(int index)
    {
        if (availableCards == null || index < 0 || index >= availableCards.Count)
            return null;

        return availableCards[index];
    }

    public int GetCardIndex(CardData data)
    {
        if (availableCards == null || data == null)
            return -1;

        return availableCards.IndexOf(data);
    }

    // Server-side test flow cho card:
    // Client bấm card -> server validate lượt/card/target -> trừ Uses/Cooldown -> sync lại UI.
    // Bản này cố ý chưa apply gameplay effect và không còn phụ thuộc CardEffectType/effectValue trong CardData.
    public bool ProcessCardRequest(PlayerRef player, PlayerNetworkController controller, int handIndex, Vector2Int targetPos)
    {
        if (!HasStateAuthority || controller == null)
            return false;

        if (handIndex < 0 || handIndex >= controller.HandCards.Length)
        {
            Debug.LogWarning($"[Server Card] Rejected card request: invalid handIndex={handIndex} from {player}.");
            return false;
        }

        NetworkCardInstance cardInstance = controller.HandCards[handIndex];
        if (!cardInstance.isInitialized)
        {
            Debug.LogWarning($"[Server Card] Rejected card request: slot {handIndex} is not initialized.");
            return false;
        }

        if (cardInstance.currentCooldown > 0 || cardInstance.remainingUses <= 0)
        {
            Debug.LogWarning($"[Server Card] Rejected card request: card not ready. Cooldown={cardInstance.currentCooldown}, Uses={cardInstance.remainingUses}.");
            return false;
        }

        CardData data = GetCardData(cardInstance.cardDataIndex);
        if (data == null)
        {
            Debug.LogWarning($"[Server Card] Rejected card request: cardDataIndex={cardInstance.cardDataIndex} not found in availableCards.");
            return false;
        }

        if (!ValidateTargetIfNeeded(data, targetPos))
            return false;

        cardInstance.remainingUses--;
        cardInstance.currentCooldown = Mathf.Max(0, data.baseCooldown);
        controller.HandCards.Set(handIndex, cardInstance);

        Debug.Log($"[Server Card] Player {player} used test card '{data.cardName}'. RemainingUses={cardInstance.remainingUses}, Cooldown={cardInstance.currentCooldown}.");
        return true;
    }

    private bool ValidateTargetIfNeeded(CardData data, Vector2Int targetPos)
    {
        if (string.IsNullOrEmpty(data.requiredTargetName))
            return true;

        if (targetPos.x < 0 || targetPos.y < 0)
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' requires target containing '{data.requiredTargetName}', but client sent no target.");
            return false;
        }

        if (ServerBoardManager.Instance == null)
            return false;

        ChessPieceRuntime targetRuntime = ServerBoardManager.Instance.GetRuntimeAt(targetPos);
        if (targetRuntime == null || targetRuntime.baseData == null)
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' target at {targetPos} is empty or invalid.");
            return false;
        }

        if (!targetRuntime.baseData.pieceName.Contains(data.requiredTargetName))
        {
            Debug.LogWarning($"[Server Card] Card '{data.cardName}' requires target containing '{data.requiredTargetName}', got '{targetRuntime.baseData.pieceName}'.");
            return false;
        }

        return true;
    }
}
