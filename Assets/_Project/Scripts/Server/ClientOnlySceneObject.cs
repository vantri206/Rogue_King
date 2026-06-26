using UnityEngine;

/// <summary>
/// Put this on UI/objects that should exist only in player/client scenes.
/// It is a safety net for visual server builds so client menu/game UI cannot be clicked on the server window.
/// </summary>
public class ClientOnlySceneObject : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private bool destroyOnServer = true;
    [SerializeField] private bool alsoHideInBatchMode = true;

    private void Awake()
    {
        if (root == null)
            root = gameObject;

        if (!IsServerProcess())
            return;

        if (destroyOnServer)
        {
            Destroy(root);
        }
        else
        {
            root.SetActive(false);
        }
    }

    private bool IsServerProcess()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "-server", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-dedicated", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return alsoHideInBatchMode && Application.isBatchMode;
    }
}
