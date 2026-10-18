using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Scene;

/// <summary>
/// Malhas das partes da cena que não são o material da fresa: peça do torno, ferramentas,
/// porta-ferramentas e fixações. Medidas em mm, nas coordenadas do programa.
/// Geometria simplificada (ilustrativa), sem compromisso de colisão.
/// </summary>
public static class SceneMeshes
{
    /// <summary>Divisões da volta completa nas peças de revolução.</summary>
    public const int RevolutionSegments = 48;

    // ── Torno ──

    /// <summary>
    /// Do sistema do torno (X = raio, Z = eixo da peça) para a cena (Z para cima): o eixo da peça fica
    /// deitado em X (para a direita, como no painel do CNC) e o raio X do torno aponta para cima.
    /// É uma rotação (mantém o sentido das faces).
    /// </summary>
    public static Point3 LatheToWorld(Point3 p) => new(p.Z, -p.Y, p.X);

    /// <summary>
    /// Placa de 3 castanhas segurando a barra pela ponta de menor Z (coordenadas da cena). Usa o
    /// diâmetro e a altura da placa do setup, se houver; senão, medidas proporcionais à barra.
    /// </summary>
    public static (SolidMesh Body, SolidMesh Jaws) LatheChuck(TurningStock stock, SetupFixture? chuck, ScenePalette palette)
    {
        double r = stock.Radius;
        double bodyRadius = chuck is { Kind: SetupFixtureKind.ThreeJawChuck } c1 ? Math.Max(c1.Width / 2, r * 1.3) : Math.Max(r * 1.8, r + 20);
        double bodyLength = chuck is { Kind: SetupFixtureKind.ThreeJawChuck } c2 ? c2.Height : Math.Max(20, bodyRadius * 0.45);
        double jawThickness = chuck?.JawThickness ?? Math.Max(10, r * 0.4);
        double grip = Math.Clamp(stock.MaxZ - stock.MinZ, 1, 1e9) * 0.18;
        grip = Math.Clamp(grip, 5, 25);
        double z0 = stock.MinZ;

        var body = SolidMesh.Create(RevolutionSegments * 4);
        Revolve(body, [(0, z0 - bodyLength), (bodyRadius, z0 - bodyLength), (bodyRadius, z0), (0, z0)], 0, 0, palette.Fixture);

        var jaws = SolidMesh.Create(18);
        double jawLength = Math.Max(jawThickness * 1.6, (bodyRadius - r) * 0.6);
        for (int j = 0; j < 3; j++)
        {
            double angle = (90 + j * 120) * Math.PI / 180; // uma castanha em cima
            Box(jaws, new Point3(r, -jawThickness / 2, z0), new Point3(jawLength, jawThickness, grip), palette.FixtureJaw, angle, 0, 0);
        }

        return (body.Transformed(LatheToWorld), jaws.Transformed(LatheToWorld));
    }

    /// <summary>Peça do torno (revolução em volta de Z), com o quarto de +X/+Y removido se "quarterSection".</summary>
    public static SolidMesh Turning(TurningStock stock, bool quarterSection, ScenePalette palette)
    {
        var mesh = SolidMesh.Create(stock.Slices * 4);
        uint machined = palette.Machined(0.35, 0);
        // Peça em coordenadas da cena (eixo deitado, raio para cima)
        LatheSolidRenderer.EmitQuads(stock, quarterSection, (a, b, c, d, n, surface) =>
            mesh.AddQuad(LatheToWorld(a), LatheToWorld(b), LatheToWorld(c), LatheToWorld(d), LatheToWorld(n), surface switch
            {
                LatheSurface.Stock => palette.Stock,
                LatheSurface.Section => palette.Section,
                _ => machined,
            }));
        return mesh;
    }

    // ── Ferramenta da fresa ──

    /// <summary>
    /// Parte cortante e porta-ferramenta, com a ponta na origem e o eixo em +Z. Sem cadastro no
    /// setup, usa comprimentos padrão proporcionais ao diâmetro.
    /// </summary>
    public static (SolidMesh Cutter, SolidMesh Holder) MillTool(ToolProfile profile, SetupTool? tool, ScenePalette palette)
    {
        double r = profile.Radius;
        double tipHeight = profile.HeightAt(r);
        double cutting = Math.Max(tool?.CuttingLength ?? Math.Max(20, profile.Diameter * 2.5), tipHeight + Math.Max(1, r * 0.2));
        double holderRadius = Math.Max((tool?.HolderDiameter ?? Math.Max(profile.Diameter * 1.8, 20)) / 2, r);
        double holderLength = tool?.HolderLength ?? 45;

        var outline = new List<(double R, double Z)> { (0, 0) };
        switch (profile.Shape)
        {
            case ToolTipShape.Ball:
            case ToolTipShape.BullNose when profile.CornerRadius > 0:
            {
                // Curva da ponta amostrada pelo próprio perfil (mesma regra da remoção de material)
                double flat = profile.Shape == ToolTipShape.Ball ? 0 : Math.Max(0, r - profile.CornerRadius);
                if (flat > 0)
                {
                    outline.Add((flat, 0));
                }

                const int steps = 10;
                for (int k = 1; k <= steps; k++)
                {
                    // Ângulo uniforme no canto arredondado (pontos mais densos perto da lateral)
                    double angle = Math.PI / 2 * k / steps;
                    double d = flat + (r - flat) * Math.Sin(angle);
                    outline.Add((d, profile.HeightAt(d)));
                }

                break;
            }

            case ToolTipShape.Cone:
                outline.Add((r, tipHeight));
                break;

            default:
                outline.Add((r, 0));
                break;
        }

        outline.Add((r, cutting));
        outline.Add((0, cutting));

        var cutter = SolidMesh.Create(RevolutionSegments * outline.Count);
        Revolve(cutter, outline, 0, 0, palette.Tool);

        var holder = SolidMesh.Create(RevolutionSegments * 4);
        Revolve(holder, [(0, cutting), (holderRadius, cutting), (holderRadius, cutting + holderLength), (0, cutting + holderLength)], 0, 0, palette.Holder);
        return (cutter, holder);
    }

    // ── Ferramenta do torno ──

    /// <summary>
    /// Inserto rômbico de 80° (12,7 mm) com a ponta na origem, no plano XZ do torno (X = raio,
    /// Z = eixo), corpo para +X e +Z, e a haste do suporte saindo em +X. Devolvido já em
    /// coordenadas da cena (<see cref="LatheToWorld"/>).
    /// </summary>
    public static (SolidMesh Insert, SolidMesh Holder) LatheTool(ScenePalette palette)
    {
        const double inscribed = 12.7, thickness = 4.76, tipAngle = 80;
        double side = inscribed / Math.Sin(tipAngle * Math.PI / 180);
        double bisector = Math.PI / 4, half = tipAngle / 2 * Math.PI / 180;
        (double X, double Z) a = (side * Math.Cos(bisector + half), side * Math.Sin(bisector + half));
        (double X, double Z) b = (side * Math.Cos(bisector - half), side * Math.Sin(bisector - half));
        (double X, double Z) c = (a.X + b.X, a.Z + b.Z);

        var insert = SolidMesh.Create(8);
        Prism(insert, [(0, 0), b, c, a], -thickness / 2, thickness / 2, palette.Tool);

        // Haste de 20 mm por baixo do inserto, saindo em +X a partir do meio dele
        double centerX = c.X / 2, centerZ = c.Z / 2;
        var holder = SolidMesh.Create(8);
        Box(holder, new Point3(centerX, -thickness / 2 - 10, centerZ - 10), new Point3(70, 10, 20), palette.Holder);
        return (insert.Transformed(LatheToWorld), holder.Transformed(LatheToWorld));
    }

    // ── Fixações ──

    /// <summary>
    /// Base e mordentes de uma fixação do setup (morsa, grampo ou placa de 3 castanhas), na mesma
    /// representação simplificada do backplot 2D. Os mordentes precisam do bruto para se posicionar.
    /// </summary>
    public static (SolidMesh Base, SolidMesh Jaws) Fixture(SetupFixture f, SetupStock? stock, ScenePalette palette)
    {
        var body = SolidMesh.Create(24);
        var jaws = SolidMesh.Create(24);

        if (f.Kind == SetupFixtureKind.ThreeJawChuck)
        {
            double radius = f.Width / 2;
            double cx = f.X + radius, cy = f.Y + f.Depth / 2;
            Revolve(body, [(0, f.Z), (radius, f.Z), (radius, f.Z + f.Height), (0, f.Z + f.Height)], cx, cy, palette.Fixture);
            if (stock is null)
            {
                return (body, jaws);
            }

            double stockRadius = stock.Kind == SetupStockKind.Prism ? Math.Max(stock.Width, stock.Depth) / 2 : stock.Diameter / 2;
            double jawLength = Math.Max(f.JawThickness * 1.65, radius * 0.24);
            double jawWidth = Math.Max(f.JawThickness * 0.55, radius * 0.085);
            double jawHeight = Math.Min(f.JawThickness, stock.Height);
            double r = Math.Clamp(Math.Max(stockRadius + jawLength * 0.30, f.JawOpening / 2), 0, Math.Max(0, radius - jawLength / 2));
            for (int j = 0; j < 3; j++)
            {
                double angle = (j * 120 + f.Rotation) * Math.PI / 180;
                double x = cx + Math.Cos(angle) * r, y = cy + Math.Sin(angle) * r;

                // Castanha em dois níveis (base radial e ressalto de contato), alinhada com o raio
                Box(jaws, new Point3(x - jawLength / 2, y - jawWidth / 2, stock.Z), new Point3(jawLength, jawWidth, jawHeight * 0.55),
                    palette.FixtureJaw, angle, x, y);
                Box(jaws, new Point3(x - jawLength * 0.42, y - jawWidth * 0.40, stock.Z + jawHeight * 0.55),
                    new Point3(jawLength * 0.55, jawWidth * 0.80, jawHeight * 0.45), palette.FixtureJaw, angle, x, y);
            }

            return (body, jaws);
        }

        Box(body, new Point3(f.X, f.Y, f.Z), new Point3(f.Width, f.Depth, f.Height), palette.Fixture);
        if (f.Kind == SetupFixtureKind.Vise)
        {
            double jaw = Math.Min(f.JawThickness, f.Depth / 4);
            double opening = Math.Min(f.JawOpening, f.Depth - 2 * jaw);
            double margin = Math.Max(0, (f.Depth - opening - 2 * jaw) / 2);
            double z = f.Z + f.Height;
            Box(jaws, new Point3(f.X, f.Y + margin, z), new Point3(f.Width, jaw, jaw), palette.FixtureJaw);
            Box(jaws, new Point3(f.X, f.Y + margin + jaw + opening, z), new Point3(f.Width, jaw, jaw), palette.FixtureJaw);
        }
        else
        {
            Box(jaws, new Point3(f.X + f.Width * 0.2, f.Y + f.Depth * 0.2, f.Z + f.Height),
                new Point3(f.Width * 0.6, f.Depth * 0.6, f.JawThickness), palette.FixtureJaw);
        }

        return (body, jaws);
    }

    // ── Formas básicas ──

    /// <summary>
    /// Sólido de revolução em volta do eixo vertical que passa por (cx, cy): o contorno vai do eixo
    /// embaixo, no sentido anti-horário no plano (raio, z), até o eixo em cima.
    /// </summary>
    internal static void Revolve(SolidMesh mesh, IReadOnlyList<(double R, double Z)> outline, double cx, double cy, uint color)
    {
        const int n = RevolutionSegments;
        double step = 2 * Math.PI / n;
        for (int i = 0; i + 1 < outline.Count; i++)
        {
            (double r0, double z0) = outline[i];
            (double r1, double z1) = outline[i + 1];
            if (Math.Abs(r1 - r0) < 1e-12 && Math.Abs(z1 - z0) < 1e-12)
            {
                continue;
            }

            // Normal para fora no plano (raio, z) de um contorno anti-horário
            double nr = z1 - z0, nz = -(r1 - r0);
            for (int k = 0; k < n; k++)
            {
                double a0 = k * step, a1 = a0 + step, mid = a0 + step / 2;
                Point3 At(double r, double z, double a) => new(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z);
                mesh.AddQuad(At(r0, z0, a0), At(r0, z0, a1), At(r1, z1, a1), At(r1, z1, a0),
                    new Point3(nr * Math.Cos(mid), nr * Math.Sin(mid), nz), color);
            }
        }
    }

    /// <summary>Caixa alinhada (canto mínimo + tamanho), girada "angle" radianos em Z em volta de (px, py).</summary>
    internal static void Box(SolidMesh mesh, Point3 min, Point3 size, uint color, double angle = 0, double px = 0, double py = 0)
    {
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        Point3 P(double x, double y, double z)
        {
            double dx = min.X + x - px, dy = min.Y + y - py;
            return angle == 0 ? new Point3(min.X + x, min.Y + y, min.Z + z) : new Point3(px + dx * cos - dy * sin, py + dx * sin + dy * cos, min.Z + z);
        }

        Point3 N(double x, double y, double z) => new(x * cos - y * sin, x * sin + y * cos, z);
        double w = size.X, d = size.Y, h = size.Z;
        mesh.AddQuad(P(0, 0, 0), P(w, 0, 0), P(w, d, 0), P(0, d, 0), N(0, 0, -1), color);
        mesh.AddQuad(P(0, 0, h), P(w, 0, h), P(w, d, h), P(0, d, h), N(0, 0, 1), color);
        mesh.AddQuad(P(0, 0, 0), P(w, 0, 0), P(w, 0, h), P(0, 0, h), N(0, -1, 0), color);
        mesh.AddQuad(P(0, d, 0), P(w, d, 0), P(w, d, h), P(0, d, h), N(0, 1, 0), color);
        mesh.AddQuad(P(0, 0, 0), P(0, d, 0), P(0, d, h), P(0, 0, h), N(-1, 0, 0), color);
        mesh.AddQuad(P(w, 0, 0), P(w, d, 0), P(w, d, h), P(w, 0, h), N(1, 0, 0), color);
    }

    /// <summary>Prisma de base convexa no plano XZ (pontos em ordem), de y0 a y1.</summary>
    internal static void Prism(SolidMesh mesh, IReadOnlyList<(double X, double Z)> polygon, double y0, double y1, uint color)
    {
        Point3 P((double X, double Z) p, double y) => new(p.X, y, p.Z);

        // Bases: quadriláteros (o inserto tem 4 lados)
        mesh.AddQuad(P(polygon[0], y0), P(polygon[1], y0), P(polygon[2], y0), P(polygon[3], y0), new Point3(0, -1, 0), color);
        mesh.AddQuad(P(polygon[0], y1), P(polygon[1], y1), P(polygon[2], y1), P(polygon[3], y1), new Point3(0, 1, 0), color);

        // Centro da base, para saber para que lado cada face lateral olha
        double cx = polygon.Average(p => p.X), cz = polygon.Average(p => p.Z);
        for (int i = 0; i < polygon.Count; i++)
        {
            (double X, double Z) a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            double nx = b.Z - a.Z, nz = -(b.X - a.X);
            if (nx * ((a.X + b.X) / 2 - cx) + nz * ((a.Z + b.Z) / 2 - cz) < 0)
            {
                nx = -nx;
                nz = -nz;
            }

            mesh.AddQuad(P(a, y0), P(b, y0), P(b, y1), P(a, y1), new Point3(nx, 0, nz), color);
        }
    }
}
