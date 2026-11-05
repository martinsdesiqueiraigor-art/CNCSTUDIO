using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// Alturas Z informadas pelo usuário para uma operação gerada a partir do modelo 3D (auditoria R-05). Na coordenada do próprio
/// modelo, que é a do programa: <see cref="SafeZ"/> é a altura de deslocamento entre pontos e <see cref="ClearanceZ"/> o plano de
/// aproximação (R). Nada é suposto a partir do modelo. Estar acima do modelo não prova ausência de colisão com morsa, grampos
/// ou castanhas.
/// </summary>
public sealed record ModelZ(double SafeZ, double ClearanceZ)
{
    /// <summary>
    /// Motivo se as alturas são incoerentes; null se servem. <paramref name="modelTopZ"/>: ponto mais alto do modelo (o deslocamento
    /// tem que passar acima dele). <paramref name="workTopZ"/>: topo da região trabalhada (a aproximação tem que ficar acima dele).
    /// </summary>
    public string? Validate(double modelTopZ, double workTopZ)
    {
        if (!double.IsFinite(SafeZ) || !double.IsFinite(ClearanceZ))
        {
            return "Alturas Z: digite números válidos (Z seguro e aproximação).";
        }

        if (ClearanceZ <= workTopZ)
        {
            return $"Alturas Z: a aproximação ({ClearanceZ:0.##}) precisa ficar acima do topo da região trabalhada (Z {workTopZ:0.##}).";
        }

        if (SafeZ <= ClearanceZ)
        {
            return $"Alturas Z: o Z seguro ({SafeZ:0.##}) precisa ficar acima da aproximação ({ClearanceZ:0.##}).";
        }

        return SafeZ <= modelTopZ ? $"Alturas Z: o Z seguro ({SafeZ:0.##}) precisa ficar acima do ponto mais alto do modelo (Z {modelTopZ:0.##})." : null;
    }
}

/// <summary>
/// Monta as alturas da varredura e do desbaste a partir do que o usuário digitou (auditoria R-05): nada de Z seguro/aproximação
/// fixos nem de fundo suposto pela base do modelo.
/// </summary>
public static class SurfaceLevels
{
    /// <summary>
    /// <paramref name="bottomZ"/>: fundo digitado. Tem que ficar abaixo do topo do modelo e não abaixo da base dele (o bruto é o
    /// prisma do modelo; abaixo disso a ferramenta sairia do bruto, e a malha não diz o que há ali: morsa, calço, mesa).
    /// </summary>
    public static (CamLevels? Levels, string? Error) Build(TriangleMesh model, ModelZ z, double bottomZ, double stepDown)
    {
        double top = model.Bounds.Max.Z;
        double baseZ = model.Bounds.Min.Z;
        if (z.Validate(top, top) is { } zError)
        {
            return (null, zError);
        }

        if (!double.IsFinite(bottomZ))
        {
            return (null, "Digite o Z do fundo (a base do modelo está em Z " + baseZ.ToString("0.##") + ", mas a malha não diz até onde cortar).");
        }

        if (bottomZ >= top)
        {
            return (null, $"O fundo (Z {bottomZ:0.##}) precisa ficar abaixo do topo do modelo (Z {top:0.##}).");
        }

        if (bottomZ < baseZ - 0.001)
        {
            return (null, $"O fundo (Z {bottomZ:0.##}) está abaixo da base do modelo (Z {baseZ:0.##}): a ferramenta sairia do bruto. Suba o fundo ou use a base.");
        }

        var levels = new CamLevels(z.SafeZ, z.ClearanceZ, top, bottomZ, stepDown);
        return levels.Validate() is { } error ? (null, error) : (levels, null);
    }
}
