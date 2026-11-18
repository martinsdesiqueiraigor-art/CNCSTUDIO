namespace CncStudio.Core.Simulation;

/// <summary>Forma da seção da barra do torno que não é redonda (WEB-01 S6).</summary>
public enum LatheBarShape
{
    /// <summary>Barra quadrada ou retangular (Width × Depth).</summary>
    Rectangle,

    /// <summary>Barra sextavada: Width é a medida entre faces (chave), com um canto no ângulo 0.</summary>
    Hexagon,
}

/// <summary>
/// Seção da barra do torno quadrada, retangular ou sextavada, centrada no eixo. A simulação do torno guarda só o raio de cada fatia:
/// a barra começa com o raio do canto (circunscrito) e cada passe corta por raio, como na máquina. A seção só muda o desenho: em
/// cada ângulo o material vai até o menor entre o raio da fatia e a borda da seção (cantos arredondados pelo torneamento).
/// </summary>
public sealed record LatheBarSection(LatheBarShape Shape, double Width, double Depth)
{
    /// <summary>Raio do canto mais longe do eixo.</summary>
    public double Circumradius => Shape == LatheBarShape.Hexagon ? Width / Math.Sqrt(3) : Math.Sqrt((Width * Width) + (Depth * Depth)) / 2;

    /// <summary>Raio da face mais perto do eixo.</summary>
    public double Inradius => Shape == LatheBarShape.Hexagon ? Width / 2 : Math.Min(Width, Depth) / 2;

    /// <summary>Distância do eixo até a borda da seção no ângulo (radianos, a partir de +X).</summary>
    public double RadiusAt(double angle)
    {
        if (Shape == LatheBarShape.Hexagon)
        {
            const double sector = Math.PI / 3;
            double a = angle % sector;
            if (a < 0)
            {
                a += sector;
            }

            return Width / 2 / Math.Cos(a - (sector / 2));
        }

        double c = Math.Abs(Math.Cos(angle)), s = Math.Abs(Math.Sin(angle));
        double rx = c < 1e-12 ? double.PositiveInfinity : Width / 2 / c;
        double ry = s < 1e-12 ? double.PositiveInfinity : Depth / 2 / s;
        return Math.Min(rx, ry);
    }

    /// <summary>"sextavado 46" ou "quadrado 40" ou "40 × 30".</summary>
    public string Describe()
    {
        static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
        return Shape == LatheBarShape.Hexagon ? $"sextavado {N(Width)}"
            : Math.Abs(Width - Depth) < 1e-9 ? $"quadrado {N(Width)}" : $"{N(Width)} × {N(Depth)}";
    }
}
