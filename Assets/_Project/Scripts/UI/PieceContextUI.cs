using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class PieceContextUI : MonoBehaviour
{
    [Header("Stat Bar (Top)")]
    [SerializeField] private Animator statBarAnimator;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI atkText;

    [SerializeField] private Vector3 uiOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Action Menu (Right)")]
    [SerializeField] private GameObject actionMenuContainer;
    [SerializeField] private Button moveButton;
    [SerializeField] private Button attackButton;

    private Action onMove;
    private Action onAttack;

    private static readonly int openHash = Animator.StringToHash("Open");

    private void Awake()
    {
        moveButton.onClick.AddListener(() => onMove?.Invoke());
        attackButton.onClick.AddListener(() => onAttack?.Invoke());

        gameObject.SetActive(false);
    }

    public void Show(ChessPiece piece, Action moveCallback, Action attackCallback)
    {
        onMove = moveCallback;
        onAttack = attackCallback;

        ChessPieceRuntime data = piece.pieceData;
        if (data != null)
        {
            hpText.text = $"{data.currentHealth}";
            atkText.text = $"{data.currentAttack}";
        }

        transform.position = piece.transform.position + uiOffset;

        gameObject.SetActive(true);
        actionMenuContainer.SetActive(true);

        if (statBarAnimator != null)
        {
            statBarAnimator.SetTrigger(openHash);
        }
    }

    public void Hide()
    {
        onMove = null;
        onAttack = null;

        gameObject.SetActive(false);
    }
}