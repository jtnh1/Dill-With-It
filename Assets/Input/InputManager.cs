using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Pickleball/Input Manager")]
public class InputManager : ScriptableObject
{
    private const string RebindKey = "InputRebinds";
    
    [HideInInspector] public PickleballInputActions actions;

    public void Initialize()
    {
        actions = new PickleballInputActions();
        LoadOverrides();
        actions.Enable();
    }

    public void Disable() => actions?.Disable();

    // -- Reboinding
    public void StartRebind(InputAction action, int bindingIndex, System.Action onComplete)
    {
        action.Disable();
        var op = action.PerformInteractiveRebinding(bindingIndex)
            .WithCancelingThrough("<Keyboard>/escape")
            .OnComplete(callback =>
            {
                callback.Dispose();
                action.Enable();
                SaveOverrides();
                onComplete?.Invoke();
            })
            .Start();
    }

    void SaveOverrides()
    {
        string json = actions.SaveBindingOverridesAsJson();
        PlayerPrefs.SetString(RebindKey, json);
        PlayerPrefs.Save();
    }

    void LoadOverrides()
    {
        if (PlayerPrefs.HasKey(RebindKey)) actions.LoadBindingOverridesFromJson(PlayerPrefs.GetString(RebindKey));
    }

    public string GetBindingDisplayString(InputAction action, int bindingIndex) => action.GetBindingDisplayString(bindingIndex);
}
