namespace Core.Entities;

/// <summary>
/// Representa as configurações de calibração para sticks analógicos e gatilhos de um gamepad.
/// </summary>
public class CalibrationSettings
{
    // Deadzones
    public float LeftStickDeadzoneInner { get; set; } = 0.1f;
    public float LeftStickDeadzoneOuter { get; set; } = 1.0f;

    public float RightStickDeadzoneInner { get; set; } = 0.1f;
    public float RightStickDeadzoneOuter { get; set; } = 1.0f;

    // Sensibilidade
    public float LeftStickSensitivity { get; set; } = 1.0f;
    public float RightStickSensitivity { get; set; } = 1.0f;

    // Gatilhos
    public float LeftTriggerStart { get; set; } = 0.0f;
    public float LeftTriggerEnd { get; set; } = 1.0f;
    
    public float RightTriggerStart { get; set; } = 0.0f;
    public float RightTriggerEnd { get; set; } = 1.0f;

    // Extra: ajustes por perfil futuramente
}