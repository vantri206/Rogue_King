using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

public class WeaponControllerUI : MonoBehaviour
{
    [Header("Main Panels")]
    [SerializeField] private GameObject uiPanel;

    [Header("Action Button")]
    [SerializeField] private Button actionButton;
    [SerializeField] private TextMeshProUGUI actionButtonText;

    [Header("Weapon Slots (Max 3)")]
    [SerializeField] private Button[] weaponButtons;
    [SerializeField] private GameObject[] weaponHighlights;
    [SerializeField] private TextMeshProUGUI[] weaponNameTexts;
    [SerializeField] private Image[] weaponIcons;

    public Action onActionPressed;
    public Action<int> onWeaponSelected;

    private void Awake()
    {
        if (actionButton != null) actionButton.onClick.AddListener(() => onActionPressed?.Invoke());

        for (int i = 0; i < weaponButtons.Length; i++)
        {
            int index = i;
            if (weaponButtons[i] != null)
            {
                weaponButtons[i].onClick.AddListener(() => onWeaponSelected?.Invoke(index));
            }
        }
        TogglePanel(false);
    }

    public void TogglePanel(bool active)
    {
        if (uiPanel != null) uiPanel.SetActive(active);
    }

    public void SetupWeaponSlots(List<WeaponData> equippedWeapons, int currentIndex)
    {
        for (int i = 0; i < weaponButtons.Length; i++)
        {
            if (i < equippedWeapons.Count)
            {
                weaponButtons[i].gameObject.SetActive(true);
                if (weaponNameTexts != null && weaponNameTexts.Length > i && weaponNameTexts[i] != null)
                    weaponNameTexts[i].text = equippedWeapons[i].weaponName;

                if (weaponIcons != null && weaponIcons.Length > i && weaponIcons[i] != null)
                {
                    weaponIcons[i].sprite = equippedWeapons[i].weaponIcon;
                    weaponIcons[i].gameObject.SetActive(equippedWeapons[i].weaponIcon != null);
                }
            }
            else
            {
                weaponButtons[i].gameObject.SetActive(false);
            }
        }
        UpdateActiveWeaponHighlight(currentIndex);
    }

    public void UpdateActiveWeaponHighlight(int activeIndex)
    {
        for (int i = 0; i < weaponHighlights.Length; i++)
        {
            if (weaponHighlights[i] != null)
            {
                weaponHighlights[i].SetActive(i == activeIndex);
            }
        }
    }

    public void SetActionMode(bool isConfirming)
    {
        if (actionButtonText != null)
        {
            actionButtonText.text = isConfirming ? "FIRE!" : "ATTACK";
        }

        foreach (var btn in weaponButtons)
        {
            if (btn != null) btn.interactable = !isConfirming;
        }
    }
}