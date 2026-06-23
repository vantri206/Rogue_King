using System;
using UnityEngine.UI;
using TMPro;
using UnityEngine;

public class PieceContextUI : MonoBehaviour
{
    [Header("Stat Bar")]
    [SerializeField] private Animator statBarAnimator;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI atkText;

    [Header("Legacy Health Widgets - Hidden In Online Patch")]
    [Tooltip("Old fill image is kept only so existing scene references can be auto-hidden after this patch.")]
    [SerializeField] private Image hpFillImage;

    [Tooltip("Old slider is kept only so existing scene references can be auto-hidden after this patch.")]
    [SerializeField] private Slider hpSlider;

    [SerializeField] private Vector3 uiOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Skill UI")]
    [SerializeField] private Button skillButton;
    [SerializeField] private TextMeshProUGUI skillButtonText;

    public Action<ChessPiece> OnSkillButtonClicked;

    private ChessPiece currentShowingPiece;
    private NetworkChessPiece currentShowingNetworkPiece;

    private static readonly int openHash = Animator.StringToHash("Open");

    private void Awake()
    {
        HideLegacyHealthWidgets();
        gameObject.SetActive(false);

        if (skillButton != null)
        {
            skillButton.onClick.AddListener(() =>
            {
                if (currentShowingPiece != null)
                    OnSkillButtonClicked?.Invoke(currentShowingPiece);
            });
        }
    }

    private void OnValidate()
    {
        HideLegacyHealthWidgets();
    }

    private void LateUpdate()
    {
        if (currentShowingNetworkPiece == null || !gameObject.activeSelf)
            return;

        if (!currentShowingNetworkPiece.gameObject.activeInHierarchy)
        {
            Hide();
            return;
        }

        RefreshNetworkPieceStats(currentShowingNetworkPiece);
        transform.position = currentShowingNetworkPiece.transform.position + uiOffset;
    }

    public void Show(ChessPiece piece)
    {
        if (piece == null || piece.pieceData == null) return;

        bool shouldPlayOpen = !gameObject.activeSelf || currentShowingPiece != piece;

        currentShowingNetworkPiece = null;
        currentShowingPiece = piece;

        ChessPieceRuntime data = piece.pieceData;
        UpdateHealthUI(data.currentHealth);
        if (atkText != null) atkText.text = $"{data.currentAttack}";

        transform.position = piece.transform.position + uiOffset;
        SetupSkillUI(data);
        HideLegacyHealthWidgets();
        gameObject.SetActive(true);

        if (shouldPlayOpen && statBarAnimator != null)
        {
            statBarAnimator.SetTrigger(openHash);
        }
    }

    public void Show(NetworkChessPiece piece)
    {
        if (piece == null)
        {
            Hide();
            return;
        }

        bool shouldPlayOpen = !gameObject.activeSelf || currentShowingNetworkPiece != piece;

        currentShowingPiece = null;
        currentShowingNetworkPiece = piece;

        RefreshNetworkPieceStats(piece);
        transform.position = piece.transform.position + uiOffset;

        // Online skills are not server-authoritative yet, so do not expose the old offline skill button here.
        if (skillButton != null)
            skillButton.gameObject.SetActive(false);

        HideLegacyHealthWidgets();
        gameObject.SetActive(true);

        if (shouldPlayOpen && statBarAnimator != null)
        {
            statBarAnimator.SetTrigger(openHash);
        }
    }

    public void RefreshIfShowing(NetworkChessPiece piece)
    {
        if (piece == null || currentShowingNetworkPiece != piece || !gameObject.activeSelf)
            return;

        RefreshNetworkPieceStats(piece);
    }

    private void RefreshNetworkPieceStats(NetworkChessPiece piece)
    {
        if (piece == null) return;

        ChessPieceData data = piece.PieceData;
        int attack = data != null ? data.baseAttack : 0;

        UpdateHealthUI(piece.currentHp);
        if (atkText != null) atkText.text = data != null ? $"{attack}" : "--";
    }

    private void UpdateHealthUI(int currentHp)
    {
        if (hpText != null)
            hpText.text = Mathf.Max(0, currentHp).ToString();

        HideLegacyHealthWidgets();
    }

    private void HideLegacyHealthWidgets()
    {
        if (hpFillImage != null)
            hpFillImage.gameObject.SetActive(false);

        if (hpSlider != null)
            hpSlider.gameObject.SetActive(false);
    }

    private void SetupSkillUI(ChessPieceRuntime data)
    {
        if (skillButton == null) return;

        if (data.baseData.activeSkill == SkillType.None)
        {
            skillButton.gameObject.SetActive(false);
            return;
        }

        skillButton.gameObject.SetActive(true);
        bool canUseSkill = true;
        string btnText = "USE SKILL";

        if (data.silencedTurnsLeft > 0)
        {
            canUseSkill = false;
            btnText = "SILENCED!";
        }
        else if (data.currentSkillCooldown > 0)
        {
            canUseSkill = false;
            btnText = $"CD: {data.currentSkillCooldown}";
        }
        else if (data.baseData.activeSkill == SkillType.PawnShield)
        {
            if (GameManager.Instance.hasUsedPawnShieldThisTurn)
            {
                canUseSkill = false;
                btnText = "LIMIT REACHED";
            }
            else if (data.hasShield)
            {
                canUseSkill = false;
                btnText = "SHIELD ACTIVE";
            }
        }
        else if (data.baseData.activeSkill == SkillType.KingRevive)
        {
            if (data.hasUsedRevive)
            {
                canUseSkill = false;
                btnText = "USED!";
            }
            else if (GameManager.Instance.graveyard.Find(x => x.faction == data.faction) == null)
            {
                canUseSkill = false;
                btnText = "NO DEAD ALLY";
            }
        }
        else if (data.baseData.activeSkill == SkillType.KingSweep)
        {
            if (data.sweepUsesLeft <= 0)
            {
                canUseSkill = false;
                btnText = "OUT OF USES";
            }
            else
            {
                btnText = $"CÀN QUÉT ({data.sweepUsesLeft})";
            }
        }
        else if (data.baseData.activeSkill == SkillType.KingDash)
        {
            if (data.currentSkillCooldown > 0)
            {
                canUseSkill = false;
                btnText = $"CD: {data.currentSkillCooldown}";
            }
            else
            {
                btnText = "LƯỚT NHANH";
            }
        }

        skillButton.interactable = canUseSkill;
        if (skillButtonText != null) skillButtonText.text = btnText;
    }

    public void Hide()
    {
        currentShowingPiece = null;
        currentShowingNetworkPiece = null;
        gameObject.SetActive(false);
    }
}
