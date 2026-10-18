namespace CncStudio.Core.Simulation;

/// <summary>Forma da ponta da ferramenta, vista de lado.</summary>
public enum ToolTipShape
{
    /// <summary>Topo reto (fresa de topo, fresa de facear, macho).</summary>
    Flat,

    /// <summary>Ponta esférica (raio = metade do diâmetro).</summary>
    Ball,

    /// <summary>Toroidal: fundo reto com raio de canto.</summary>
    BullNose,

    /// <summary>Ponta cônica (broca), pelo ângulo da ponta.</summary>
    Cone,
}

/// <summary>
/// Perfil de revolução da parte cortante, usado na remoção de material. A ponta da ferramenta
/// (o ponto programado) é o ponto mais baixo; <see cref="HeightAt"/> diz quanto a superfície de
/// corte sobe a uma distância radial d do eixo.
/// </summary>
public sealed record ToolProfile(ToolTipShape Shape, double Radius, double CornerRadius = 0, double TipAngle = 118)
{
    /// <summary>Topo reto com o diâmetro dado (o comportamento de antes do setup).</summary>
    public static ToolProfile Flat(double diameter) => new(ToolTipShape.Flat, Math.Max(0.01, diameter) / 2);

    public double Diameter => Radius * 2;

    /// <summary>True quando o fundo é todo plano (dispensa o cálculo por célula).</summary>
    public bool IsFlat => Shape == ToolTipShape.Flat || (Shape == ToolTipShape.BullNose && CornerRadius <= 0);

    /// <summary>Altura da superfície de corte acima da ponta, a uma distância radial d (0 ≤ d ≤ Radius).</summary>
    public double HeightAt(double d)
    {
        d = Math.Clamp(d, 0, Radius);
        switch (Shape)
        {
            case ToolTipShape.Ball:
                return Radius - Math.Sqrt(Math.Max(0, Radius * Radius - d * d));

            case ToolTipShape.BullNose:
            {
                double rc = Math.Clamp(CornerRadius, 0, Radius);
                double flat = Radius - rc;
                if (d <= flat)
                {
                    return 0;
                }

                double u = d - flat;
                return rc - Math.Sqrt(Math.Max(0, rc * rc - u * u));
            }

            case ToolTipShape.Cone:
            {
                double half = Math.Clamp(TipAngle, 1, 179) / 2 * Math.PI / 180;
                return d / Math.Tan(half);
            }

            default:
                return 0;
        }
    }
}
