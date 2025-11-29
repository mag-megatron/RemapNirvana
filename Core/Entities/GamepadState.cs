// MemberwiseClone implicitamente disponível via 'using System'.

namespace Core.Entities;
/// <summary>
/// Representa o estado instantâneo de todos os botões e eixos de um gamepad.
/// </summary>
public class GamepadState : IEquatable<GamepadState>
{
    /// <summary>
    /// Obtém ou define o estado dos botões.
    /// </summary>
    public bool ButtonA { get; set; }
    public bool ButtonB { get; set; }
    public bool ButtonX { get; set; }
    public bool ButtonY { get; set; }

    /// <summary>
    /// Obtém ou define o estado dos D-Pads.
    /// </summary>
    public bool DPadUp { get; set; }
    public bool DPadDown { get; set; }
    public bool DPadLeft { get; set; }
    public bool DPadRight { get; set; }

    /// <summary>
    /// Obtém ou define o estado do botão Start e Back (ou Select).
    /// </summary>
    public bool ButtonStart { get; set; }
    public bool ButtonBack { get; set; }

    /// <summary>
    /// Obtém ou define o estado do botão de ombro esquerdo (LB)
    /// e direito (RB).
    /// </summary>
    public bool ButtonLeftShoulder { get; set; }
    public bool ButtonRightShoulder { get; set; }

    /// <summary>
    /// Obtém ou define o estado do botão do analógico esquerdo (pressionado)
    /// e direito (pressionado).
    /// </summary>
    public bool ThumbLPressed { get; set; }
    public bool ThumbRPressed { get; set; }

    /// <summary>
    /// Obtém ou define o valor do gatilho esquerdo (LT) e direito (RT).
    /// Varia de 0.0 (solto) a 1.0 (pressionado).
    /// </summary>
    public float TriggerLeft { get; set; }
    public float TriggerRight { get; set; }

    /// <summary>
    /// Obtém ou define a posição no eixo X e Y dos analógicos esquerdo e direito.
    /// Varia de -1.0 (esquerda) a 1.0 (direita).
    /// </summary>
    public float ThumbLX { get; set; }
    public float ThumbLY { get; set; }
    public float ThumbRX { get; set; }
    public float ThumbRY { get; set; }

    public bool Equals(GamepadState? other)
    {
        if (other is null) return false;

        return ButtonA == other.ButtonA &&
               ButtonB == other.ButtonB &&
               ButtonX == other.ButtonX &&
               ButtonY == other.ButtonY &&
               DPadUp == other.DPadUp &&
               DPadDown == other.DPadDown &&
               DPadLeft == other.DPadLeft &&
               DPadRight == other.DPadRight &&
               ButtonStart == other.ButtonStart &&
               ButtonBack == other.ButtonBack &&
               ButtonLeftShoulder == other.ButtonLeftShoulder &&
               ButtonRightShoulder == other.ButtonRightShoulder &&
               ThumbLPressed == other.ThumbLPressed &&
               ThumbRPressed == other.ThumbRPressed &&
               TriggerLeft.Equals(other.TriggerLeft) &&
               TriggerRight.Equals(other.TriggerRight) &&
               ThumbLX.Equals(other.ThumbLX) &&
               ThumbLY.Equals(other.ThumbLY) &&
               ThumbRX.Equals(other.ThumbRX) &&
               ThumbRY.Equals(other.ThumbRY);
    }

    public override bool Equals(object? obj) => Equals(obj as GamepadState);
    public override string ToString()
    {
        return $"""
        A:{ButtonA} B:{ButtonB} X:{ButtonX} Y:{ButtonY}
        DPad ↑:{DPadUp} ↓:{DPadDown} ←:{DPadLeft} →:{DPadRight}
        Start:{ButtonStart} Back:{ButtonBack}
        LB:{ButtonLeftShoulder} RB:{ButtonRightShoulder}
        L3:{ThumbLPressed} R3:{ThumbRPressed}
        LT:{TriggerLeft:F2} RT:{TriggerRight:F2}
        LX:{ThumbLX:F2} LY:{ThumbLY:F2} RX:{ThumbRX:F2} RY:{ThumbRY:F2}
    """;
    }

    public override int GetHashCode()
    {
        var hash1 = HashCode.Combine(
            ButtonA, ButtonB, ButtonX, ButtonY,
            DPadUp, DPadDown, DPadLeft, DPadRight
        );

        var hash2 = HashCode.Combine(
            ButtonStart, ButtonBack,
            ButtonLeftShoulder, ButtonRightShoulder,
            ThumbLPressed, ThumbRPressed,
            TriggerLeft, TriggerRight
        );

        var hash3 = HashCode.Combine(
            ThumbLX, ThumbLY, ThumbRX, ThumbRY
        );

        return HashCode.Combine(hash1, hash2, hash3);
    }



    /// <summary>
    /// Cria uma cópia superficial (shallow copy) do estado atual do gamepad.
    /// </summary>
    /// <returns>Um novo objeto <see cref="GamepadState"/> com os mesmos valores do atual.</returns>
    public GamepadState Clone()
    {
        return (GamepadState)this.MemberwiseClone();
    }
}