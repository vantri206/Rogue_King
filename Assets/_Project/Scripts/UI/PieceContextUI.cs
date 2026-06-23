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

    [SerializeField] private Vector3 uiOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Skill UI")]
    [SerializeField] private Button skillButton;
    [SerializeField] private TextMeshProUGUI skillButtonText;

    public Action<ChessPiece> OnSkillButtonClicked;
    private ChessPiece currentShowingPiece;

    private static readonly int openHash = Animator.StringToHash("Open");

    private void Awake()
    {
        gameObject.SetActive(false);
        if (skillButton != null)
        {
            skillButton.onClick.AddListener(() => OnSkillButtonClicked?.Invoke(currentShowingPiece));
        }
    }

    public void Show(ChessPiece piece)
    {
        if (piece == null || piece.pieceData == null) return;

        ChessPieceRuntime data = piece.pieceData;
        hpText.text = $"{data.currentHealth}";
        atkText.text = $"{data.currentAttack}";

        transform.position = piece.transform.position + uiOffset;
        currentShowingPiece = piece;
        SetupSkillUI(data);
        gameObject.SetActive(true);

        if (statBarAnimator != null)
        {
            statBarAnimator.SetTrigger(openHash);
        }

    }
    private void SetupSkillUI(ChessPieceRuntime data)
    {
        if (skillButton == null) return;



        skillButton.gameObject.SetActive(true);
        bool canUseSkill = true;
        string btnText = "USE SKILL";

        

        skillButton.interactable = canUseSkill;
        if (skillButtonText != null) skillButtonText.text = btnText;
    }
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}