using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class UIInventoryAnimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform inventoryPanel; // Kéo HandPanel vào đây
    [SerializeField] private Button toggleButton;        // Kéo ToggleInvenButton vào đây
    [SerializeField] private RectTransform arrowIcon;    // Kéo cái Image mũi tên (ArrowIcon) vào đây

    [Header("Animation Settings")]
    [SerializeField] private float duration = 0.4f;   // Tốc độ trượt/xoay
    [SerializeField] private Ease easeType = Ease.OutBack; // Hiệu ứng trượt ra có độ nảy nhẹ rất đẹp

    private Vector2 shownPosition;
    private Vector2 hiddenPosition;

    // Đặt true vì mặc định khi vào game cái bảng của bạn đang hiện trên màn hình
    private bool isOpen = true;
    private Tween currentTween;

    void Start()
    {
        // Ghi nhớ tọa độ ĐANG HIỆN tại trên Editor
        shownPosition = inventoryPanel.anchoredPosition;

        // Tính toán tọa độ ĐÓNG (Đẩy cái bảng sang trái một đoạn bằng chính chiều rộng của nó + 50 pixel dư ra)
        float hiddenX = shownPosition.x - inventoryPanel.rect.width - 50f;
        hiddenPosition = new Vector2(hiddenX, shownPosition.y);

        // Đảm bảo mũi tên đang hướng đúng lúc bắt đầu (0 độ)
        if (arrowIcon != null) arrowIcon.localEulerAngles = Vector3.zero;

        toggleButton.onClick.AddListener(OnToggleClicked);
    }

    private void OnToggleClicked()
    {
        // Chặn người chơi spam nút bấm liên tục gây loạn animation
        if (currentTween != null && currentTween.IsActive() && currentTween.IsPlaying()) return;

        Animate(!isOpen);
    }

    private void Animate(bool targetOpen)
    {
        isOpen = targetOpen;
        Vector2 targetPosition = isOpen ? shownPosition : hiddenPosition;

        // 1. CHẠY ANIMATION TRƯỢT BẢNG
        inventoryPanel.DOKill(); // Xóa animation cũ nếu có
        currentTween = inventoryPanel.DOAnchorPos(targetPosition, duration).SetEase(easeType);

        // 2. CHẠY ANIMATION XOAY MŨI TÊN
        if (arrowIcon != null)
        {
            arrowIcon.DOKill();
            // Nếu Đóng (false) thì xoay mũi tên chỉ sang phải (180 độ). Nếu Mở (true) thì xoay về 0 độ (chỉ sang trái).
            float targetRotationZ = isOpen ? 0f : 180f;

            // Xoay trục Z
            arrowIcon.DORotate(new Vector3(0, 0, targetRotationZ), duration, RotateMode.Fast).SetEase(Ease.OutCubic);
        }
    }
}