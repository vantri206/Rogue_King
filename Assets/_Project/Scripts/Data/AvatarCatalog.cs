using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "King Online/Avatar Catalog", fileName = "AvatarCatalog")]
public class AvatarCatalog : ScriptableObject
{
    [SerializeField] private List<Sprite> avatars = new List<Sprite>();
    [SerializeField] private Sprite fallbackAvatar;

    public int Count => avatars != null ? avatars.Count : 0;

    public Sprite GetAvatar(int avatarId)
    {
        if (avatars != null && avatars.Count > 0)
        {
            int safeId = Mathf.Clamp(avatarId, 0, avatars.Count - 1);
            Sprite avatar = avatars[safeId];
            if (avatar != null)
                return avatar;
        }

        return fallbackAvatar;
    }

    public int ClampAvatarId(int avatarId)
    {
        int count = Count;
        if (count <= 0)
            return 0;

        return Mathf.Clamp(avatarId, 0, count - 1);
    }

    public int GetRandomAvatarId()
    {
        int count = Count;
        if (count <= 0)
            return 0;

        return Random.Range(0, count);
    }
}
