using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using System;
using System.Collections.Generic;

[System.Serializable]
public class WeaponSlotUI
{
    public Button slotButton;

    [Tooltip("Dim/highlight overlay used for inactive, non-selected slots. Kept for backward compatibility with the old UI.")]
    public GameObject weaponOverlay;

    [Tooltip("Overlay shown when this weapon is on cooldown. This should sit above the icon/name.")]
    public GameObject cooldownOverlay;

    [Tooltip("Text shown on top of the cooldown overlay, usually a remaining turn count.")]
    public TextMeshProUGUI cooldownText;

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

    private bool unityButtonEventsBound;
    private UnityAction actionButtonHandler;
    private UnityAction[] weaponSlotHandlers;

    private readonly List<int> currentCooldowns = new List<int>();
    private bool[] slotHasWeapon;
    private bool isConfirmingAction;

    private void Awake()
    {
        BindUnityButtonEventsOnce();
    }

    private void OnDestroy()
    {
        UnbindUnityButtonEvents();
        onActionPressed = null;
        onWeaponSelected = null;
    }

    private void BindUnityButtonEventsOnce()
    {
        if (unityButtonEventsBound)
            return;

        if (actionButton != null)
        {
            actionButtonHandler = HandleActionButtonClicked;
            actionButton.onClick.RemoveListener(actionButtonHandler);
            actionButton.onClick.AddListener(actionButtonHandler);
        }

        if (weaponSlots != null)
        {
            weaponSlotHandlers = new UnityAction[weaponSlots.Length];

            for (int i = 0; i < weaponSlots.Length; i++)
            {
                int index = i;
                WeaponSlotUI slot = weaponSlots[i];
                if (slot == null || slot.slotButton == null)
                    continue;

                UnityAction handler = () => HandleWeaponSlotClicked(index);
                weaponSlotHandlers[i] = handler;

                slot.slotButton.onClick.RemoveListener(handler);
                slot.slotButton.onClick.AddListener(handler);
            }
        }

        unityButtonEventsBound = true;
    }

    private void UnbindUnityButtonEvents()
    {
        if (!unityButtonEventsBound)
            return;

        if (actionButton != null && actionButtonHandler != null)
            actionButton.onClick.RemoveListener(actionButtonHandler);

        if (weaponSlots != null && weaponSlotHandlers != null)
        {
            for (int i = 0; i < weaponSlots.Length && i < weaponSlotHandlers.Length; i++)
            {
                WeaponSlotUI slot = weaponSlots[i];
                UnityAction handler = weaponSlotHandlers[i];
                if (slot != null && slot.slotButton != null && handler != null)
                    slot.slotButton.onClick.RemoveListener(handler);
            }
        }

        unityButtonEventsBound = false;
    }

    private void HandleActionButtonClicked()
    {
        onActionPressed?.Invoke();
    }

    private void HandleWeaponSlotClicked(int index)
    {
        onWeaponSelected?.Invoke(index);
    }

    public void TogglePanel(bool active)
    {
        if (weaponHUD != null) weaponHUD.SetActive(active);
    }

    public void SetupWeaponSlots(IReadOnlyList<WeaponData> weapons)
    {
        if (weaponSlots == null) return;

        slotHasWeapon = new bool[weaponSlots.Length];
        EnsureCooldownListSize(weaponSlots.Length);

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            WeaponSlotUI slot = weaponSlots[i];
            if (slot == null) continue;

            WeaponData weapon = weapons != null && i < weapons.Count ? weapons[i] : null;
            bool hasWeapon = weapon != null;
            slotHasWeapon[i] = hasWeapon;

            if (slot.slotButton != null)
            {
                slot.slotButton.gameObject.SetActive(hasWeapon);
                slot.slotButton.interactable = hasWeapon;
            }

            if (slot.weaponNameText != null)
            {
                slot.weaponNameText.text = hasWeapon ? weapon.weaponName : string.Empty;
                slot.weaponNameText.gameObject.SetActive(hasWeapon);
            }

            if (slot.weaponIcon != null)
            {
                slot.weaponIcon.sprite = hasWeapon ? weapon.weaponIcon : null;
                slot.weaponIcon.enabled = hasWeapon && weapon.weaponIcon != null;
                slot.weaponIcon.gameObject.SetActive(hasWeapon);
            }

            if (slot.weaponOverlay != null)
                slot.weaponOverlay.SetActive(false);
        }

        RefreshCooldownVisuals();
    }

    public void UpdateActiveWeaponHighlight(int activeIndex)
    {
        if (weaponSlots == null) return;

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            WeaponSlotUI slot = weaponSlots[i];
            if (slot == null) continue;

            bool slotIsVisible = slot.slotButton != null && slot.slotButton.gameObject.activeSelf;

            if (slot.weaponOverlay != null)
            {
                // Overlay is treated as a dim layer for inactive slots.
                // Cooldown has its own overlay so inactive highlight and cooldown can coexist.
                slot.weaponOverlay.SetActive(slotIsVisible && i != activeIndex);
            }
        }
    }

    public void SetActionMode(bool isConfirming)
    {
        isConfirmingAction = isConfirming;

        if (actionButtonText != null)
            actionButtonText.text = isConfirming ? "FIRE!" : "ATK";

        RefreshCooldownVisuals();
    }

    public void SetWeaponCooldowns(IReadOnlyList<int> cooldowns)
    {
        int targetCount = weaponSlots != null ? weaponSlots.Length : 0;
        EnsureCooldownListSize(targetCount);

        for (int i = 0; i < currentCooldowns.Count; i++)
        {
            int value = cooldowns != null && i < cooldowns.Count ? cooldowns[i] : 0;
            currentCooldowns[i] = Mathf.Max(0, value);
        }

        RefreshCooldownVisuals();
    }

    public bool IsWeaponOnCooldown(int weaponIndex)
    {
        return weaponIndex >= 0 && weaponIndex < currentCooldowns.Count && currentCooldowns[weaponIndex] > 0;
    }

    private void EnsureCooldownListSize(int size)
    {
        size = Mathf.Max(0, size);
        while (currentCooldowns.Count < size)
            currentCooldowns.Add(0);

        while (currentCooldowns.Count > size)
            currentCooldowns.RemoveAt(currentCooldowns.Count - 1);
    }

    private void RefreshCooldownVisuals()
    {
        if (weaponSlots == null) return;

        EnsureCooldownListSize(weaponSlots.Length);

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            WeaponSlotUI slot = weaponSlots[i];
            if (slot == null) continue;

            bool hasWeapon = slotHasWeapon != null && i < slotHasWeapon.Length && slotHasWeapon[i];
            int cooldown = i < currentCooldowns.Count ? currentCooldowns[i] : 0;
            bool onCooldown = hasWeapon && cooldown > 0;

            if (slot.cooldownOverlay != null)
                slot.cooldownOverlay.SetActive(onCooldown);

            if (slot.cooldownText != null)
            {
                slot.cooldownText.text = onCooldown ? cooldown.ToString() : string.Empty;
                slot.cooldownText.gameObject.SetActive(onCooldown);
            }

            if (slot.slotButton != null && slot.slotButton.gameObject.activeSelf)
                slot.slotButton.interactable = hasWeapon && !isConfirmingAction && !onCooldown;
        }
    }
}
