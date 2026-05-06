using UnityEngine;
using System;

public class StaminaSystem : MonoBehaviour
{
    [Header("Settings")]
    public float maxStamina = 100f;
    public float sprintDrainRate = 15f;
    public float smashCost = 25f;
    public float regenRate = 10f;
    public float regenDelay = 1.5f;
    public float CurrentStamina { get; private set; }
    public event Action<float, float> OnStaminaChanged;
    private float _regenTimer = 0f;
    private bool _isSprinting = false;
    private void Awake() => CurrentStamina = maxStamina;
    private void Update()
    {
        if (_isSprinting)
        {
            Drain(sprintDrainRate * Time.deltaTime);
            _regenTimer = regenDelay;
        } else
        {
            _regenTimer -= Time.deltaTime;
            if (_regenTimer <= 0f) Regen(regenRate * Time.deltaTime);
        }
    }

    public void SetSprinting(bool sprinting) => _isSprinting = sprinting && CurrentStamina >= 0f;
    public bool CanSprint() => CurrentStamina > 0f;
    public bool HasEnoughForSmash() => CurrentStamina >= smashCost;
    public void ConsumeSmash()
    {
        Drain(smashCost);
        _regenTimer = regenDelay;
    }

    public void ResetStamina()
    {
        CurrentStamina = maxStamina;
        _regenTimer = 0f;
        _isSprinting = false;
        OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
    }

    void Drain(float amount)
    {
        CurrentStamina = Mathf.Max(0f, CurrentStamina - amount);
        OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
        if (CurrentStamina == 0f) _isSprinting = false; 
    }

    void Regen(float amount)
    {
        CurrentStamina = Mathf.Min(maxStamina, CurrentStamina + amount);
        OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
    }
}
