using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(WeaponData))]
public class WeaponDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        WeaponData weapon = (WeaponData)target;

        weapon.weaponName = EditorGUILayout.TextField("Weapon Name", weapon.weaponName);
        weapon.attackMechanism = (AttackMechanism)EditorGUILayout.EnumPopup("Attack Mechanism", weapon.attackMechanism);
        weapon.defaultEffectType = (EffectType)EditorGUILayout.EnumPopup("Default Effect", weapon.defaultEffectType);

        EditorGUILayout.Space();

        if (weapon.attackMechanism == AttackMechanism.GridPattern)
        {
            EditorGUILayout.LabelField("Grid Pattern Settings", EditorStyles.boldLabel);
            weapon.baseDamage = EditorGUILayout.IntField("Base Damage", weapon.baseDamage);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Attack Pattern Grid (Facing Up/Forward)", EditorStyles.boldLabel);

            int newSize = EditorGUILayout.IntSlider("Grid Size", weapon.patternSize, 3, 15);
            if (newSize % 2 == 0) newSize++;

            if (newSize != weapon.patternSize || weapon.attackPatternGrid == null || weapon.attackPatternGrid.Length != newSize * newSize)
            {
                weapon.patternSize = newSize;
                weapon.attackPatternGrid = new bool[newSize * newSize];
            }

            int centerIndex = weapon.patternSize / 2;

            for (int y = 0; y < weapon.patternSize; y++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                for (int x = 0; x < weapon.patternSize; x++)
                {
                    int index = y * weapon.patternSize + x;
                    bool isCenter = (x == centerIndex && y == centerIndex);

                    GUI.backgroundColor = isCenter ? Color.yellow : Color.white;

                    if (isCenter)
                    {
                        GUILayout.Toggle(true, "X", "Button", GUILayout.Width(25), GUILayout.Height(25));
                    }
                    else
                    {
                        weapon.attackPatternGrid[index] = GUILayout.Toggle(weapon.attackPatternGrid[index], "", "Button", GUILayout.Width(25), GUILayout.Height(25));
                    }

                    GUI.backgroundColor = Color.white;
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(weapon);
        }
    }
}