using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

[System.Serializable]
public class WeaponSlotUI
{
    public Button slotButton;
    public GameObject weaponOverlay;
    public TextMeshProUGUI weaponNameText;
    public Image weaponIcon;
}

public class WeaponControllerUI : MonoBehaviour
{
    [Header("Main Panels")]
    [SerializeField] private GameObject weaponHUD;

    [Header("Action Button")]
    [SerializeField] private Button actionButton;
    [SerializeField] private TextMeshProUGUI actionButtonText;

    [Header("Weapon Slots")]
    [SerializeField] private WeaponSlotUI[] weaponSlots;

    public Action onActionPressed;
    public Action<int> onWeaponSelected;

    private void Awake()
    {
        if (actionButton != null) actionButton.onClick.AddListener(() => onActionPressed?.Invoke());

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            int index = i;
            if (weaponSlots[i].slotButton != null)
            {
                weaponSlots[i].slotButton.onClick.AddListener(() => onWeaponSelected?.Invoke(index));
            }
        }
    }

    public void TogglePanel(bool active)
    {
        if (weaponHUD != null) weaponHUD.SetActive(active);
    }
    public void SetupWeaponSlots(List<WeaponData> weapons)
    {
        for (int i = 0; i < weaponSlots.Length; i++)
        {
            if (i < weapons.Count && weapons[i] != null)
            { 
                if (weaponSlots[i].slotButton != null)
                {
                    weaponSlots[i].slotButton.gameObject.SetActive(true);
                }

                if (weaponSlots[i].weaponNameText != null)
                {
                    weaponSlots[i].weaponNameText.text = weapons[i].weaponName;
                }

                if (weaponSlots[i].weaponIcon != null)
                {
                    weaponSlots[i].weaponIcon.sprite = weapons[i].weaponIcon;
                    weaponSlots[i].weaponIcon.enabled = true;
                }
            }
            else
            {
                if (weaponSlots[i].slotButton != null)
                {
                    weaponSlots[i].slotButton.gameObject.SetActive(false);
                }
            }
        }
    }

    public void UpdateActiveWeaponHighlight(int activeIndex)
    {
        for (int i = 0; i < weaponSlots.Length; i++)
        {
            if (weaponSlots[i].weaponOverlay != null)
            {
                weaponSlots[i].weaponOverlay.SetActive(i != activeIndex);
            }
        }
    }

    public void SetActionMode(bool isConfirming)
    {
        if (actionButtonText != null)
        {
            actionButtonText.text = isConfirming ? "FIRE!" : "ATTACK";
        }

        foreach (var slot in weaponSlots)
        {
            if (slot.slotButton != null)
                slot.slotButton.interactable = !isConfirming;
        }
    }
}