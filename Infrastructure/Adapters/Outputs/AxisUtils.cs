namespace Infrastructure.Adapters.Outputs
{
    /// <summary>
    /// Helper utilities for axis conversions used by output adapters.
    /// </summary>
    internal static class AxisUtils
    {
        /// <summary>
        /// Converts a normalized float in range [-1,1] to a short value in
        /// [-32768,32767] as expected by ViGEm/DirectInput/XInput APIs.
        /// </summary>
        public static short FloatToShort(float value)
        {
            value = Math.Clamp(value, -1f, 1f);
            return value >= 0f
                ? (short)(value * 32767)
                : (short)(value * 32768);
        }
    }
}