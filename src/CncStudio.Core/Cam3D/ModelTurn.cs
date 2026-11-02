namespace CncStudio.Core.Cam3D;

/// <summary>
/// Giro do modelo para deixar o lado de cima da usinagem em Z+ (CAD costuma exportar com Y para cima).
/// Só em X e Y: girar em Z não muda o lado de cima. Sentido pela regra da mão direita.
/// </summary>
public enum ModelTurn
{
    /// <summary>+90° em torno de X: o que estava em +Y vai para +Z.</summary>
    XPlus90,

    /// <summary>−90° em torno de X: o que estava em −Y vai para +Z.</summary>
    XMinus90,

    /// <summary>+90° em torno de Y: o que estava em −X vai para +Z.</summary>
    YPlus90,

    /// <summary>−90° em torno de Y: o que estava em +X vai para +Z.</summary>
    YMinus90,

    /// <summary>Vira de cabeça para baixo (180° em torno de X).</summary>
    Flip,
}
