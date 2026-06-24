using System.Collections;
using UnityEngine;

/// <summary>
/// Attach this to a PlayScene-only debug root/canvas/object that must be visible only in the visual server build.
/// Clients will destroy or hide it after the NetworkRunnerHandler is available.
/// </summary>
public class ServerOnlySceneObject : MonoBehaviour
{
    [Header("Visibility")]
    [SerializeField] private bool destroyOnNonServer = true;
    [SerializeField] private bool hideRenderersWhileWaiting = true;
    [SerializeField] private float waitForRunnerSeconds = 3f;

    private Renderer[] cachedRenderers;
    private Canvas[] cachedCanvases;

    private void Awake()
    {
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedCanvases = GetComponentsInChildren<Canvas>(true);

        if (hideRenderersWhileWaiting)
            SetVisible(false);
    }

    private IEnumerator Start()
    {
        float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, waitForRunnerSeconds);

        while (Time.realtimeSinceStartup < deadline)
        {
            NetworkRunnerHandler handler = NetworkRunnerHandler.Active;
            if (handler != null && handler.HasRunnerStarted)
                break;

            yield return null;
        }

        bool isServer = NetworkRunnerHandler.Active != null && NetworkRunnerHandler.Active.IsServerRunner;
        if (isServer)
        {
            SetVisible(true);
            yield break;
        }

        if (destroyOnNonServer)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    private void SetVisible(bool visible)
    {
        if (cachedRenderers != null)
        {
            foreach (Renderer item in cachedRenderers)
            {
                if (item != null)
                    item.enabled = visible;
            }
        }

        if (cachedCanvases != null)
        {
            foreach (Canvas item in cachedCanvases)
            {
                if (item != null)
                    item.enabled = visible;
            }
        }
    }
}
