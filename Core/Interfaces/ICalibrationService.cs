using Core.Entities;

namespace Core.Interfaces;

public interface ICalibrationService
{
    float ApplyDeadzone(float input, float deadzone);
    float AdjustSensitivity(float input, float sensitivity);
    GamepadState Calibrate(GamepadState rawState, CalibrationSettings settings);
}