using UnityEngine;
using TMPro;

public class PieceContextUI : MonoBehaviour
{
    [Header("Stat Bar")]
    [SerializeField] private Animator statBarAnimator;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI atkText;

    [SerializeField] private Vector3 uiOffset = new Vector3(0f, 1.2f, 0f);

    private static readonly int openHash = Animator.StringToHash("Open");

    private void Awake()
    {
        gameObject.SetActive(false);
    }

    public void Show(ChessPiece piece)
    {
        if (piece == null || piece.pieceData == null) return;

        ChessPieceRuntime data = piece.pieceData;
        hpText.text = $"{data.currentHealth}";
        atkText.text = $"{data.currentAttack}";

        transform.position = piece.transform.position + uiOffset;

        gameObject.SetActive(true);

        if (statBarAnimator != null)
        {
            statBarAnimator.SetTrigger(openHash);
        }
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}