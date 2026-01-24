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

        /// <summary>
        /// Maps a circular stick vector to a square response while preserving radius.
        /// This boosts diagonals for games that interpret stick input as square.
        /// </summary>
        public static (float x, float y) CircleToSquare(float x, float y)
        {
            var max = MathF.Max(MathF.Abs(x), MathF.Abs(y));
            if (max <= 0f)
                return (0f, 0f);

            var r = MathF.Sqrt(x * x + y * y);
            if (r <= 0f)
                return (0f, 0f);

            var scale = r / max;
            var nx = Math.Clamp(x * scale, -1f, 1f);
            var ny = Math.Clamp(y * scale, -1f, 1f);
            return (nx, ny);
        }
    }
}
