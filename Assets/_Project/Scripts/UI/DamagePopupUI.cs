using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Lightweight world-space damage popup.
/// Can be used on a prefab, or auto-created at runtime by DamagePopupManager.
/// </summary>
public class DamagePopupUI : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] private TMP_Text damageText;
    [SerializeField] private string damageFormat = "-{0}";
    [SerializeField] private Color damageColor = Color.red;

    [Header("Motion")]
    [SerializeField] private float lifetime = 0.85f;
    [SerializeField] private Vector3 riseOffset = new Vector3(0f, 0.75f, 0f);
    [SerializeField] private AnimationCurve riseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private bool faceMainCamera = false;

    private Coroutine playRoutine;

    public void BindRuntimeText(TMP_Text runtimeText)
    {
        damageText = runtimeText;
    }

    public void Play(int damageAmount)
    {
        Play(damageAmount, damageColor, lifetime, riseOffset);
    }

    public void Play(int damageAmount, Color color, float duration, Vector3 moveOffset)
    {
        if (damageText == null)
            damageText = GetComponentInChildren<TMP_Text>(true);

        if (damageText != null)
        {
            damageText.text = string.Format(damageFormat, Mathf.Max(0, damageAmount));
            damageText.color = color;
            damageText.alpha = color.a;
        }

        if (playRoutine != null)
            StopCoroutine(playRoutine);

        playRoutine = StartCoroutine(PlayRoutine(color, Mathf.Max(0.05f, duration), moveOffset));
    }

    private IEnumerator PlayRoutine(Color baseColor, float duration, Vector3 moveOffset)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + moveOffset;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float motionT = riseCurve != null ? riseCurve.Evaluate(t) : t;

            transform.position = Vector3.LerpUnclamped(startPos, endPos, motionT);

            if (damageText != null)
            {
                Color c = baseColor;
                c.a = Mathf.Lerp(baseColor.a, 0f, t);
                damageText.color = c;
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    private void LateUpdate()
    {
        if (!faceMainCamera || Camera.main == null)
            return;

        transform.rotation = Camera.main.transform.rotation;
    }
}
