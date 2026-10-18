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
    /// O corpo tem chapa traseira, chanfro, rebaixo central e os 3 furos da chave; cada castanha é
    /// de dois níveis (base que corre no rasgo e ressalto de contato).
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

        // Contorno do corpo da face (z0) para trás (z0 − bodyLength): chapa traseira menor, corpo principal,
        // chanfro na frente e um rebaixo no meio da face
        double zBack = z0 - bodyLength, plate = bodyLength * 0.28;
        double recess = Math.Min(bodyRadius * 0.55, Math.Max(r * 1.25, bodyRadius * 0.3));
        double chamfer = Math.Min(bodyRadius * 0.06, 6);
        var bodyColors = new uint[8];
        Array.Fill(bodyColors, palette.Fixture);
        bodyColors[1] = MillSolidRenderer.Lerp(palette.Fixture, 0x000000, 0.18);   // chapa traseira
        bodyColors[2] = MillSolidRenderer.Lerp(palette.Fixture, 0x000000, 0.18);
        bodyColors[5] = MillSolidRenderer.Lerp(palette.Fixture, 0xFFFFFF, 0.10);   // face
        var body = SolidMesh.Create(RevolutionSegments * 8);
        Revolve(body,
        [
            (0, zBack),
            (bodyRadius * 0.86, zBack),
            (bodyRadius * 0.86, zBack + plate),
            (bodyRadius, zBack + plate),
            (bodyRadius, z0 - chamfer),
            (bodyRadius - chamfer, z0),
            (recess, z0),
            (0, z0),
        ], 0, 0, palette.Fixture, bodyColors);

        var jaws = SolidMesh.Create(40);
        double jawLength = Math.Max(jawThickness * 1.6, (bodyRadius - r) * 0.6);
        double socket = Math.Clamp(bodyRadius * 0.07, 2.5, 9);
        for (int j = 0; j < 3; j++)
        {
            double angle = (90 + j * 120) * Math.PI / 180; // uma castanha em cima
            double lowLength = jawLength;
            // Base da castanha (corre no rasgo) e ressalto de contato com a peça, mais perto do centro
            Box(jaws, new Point3(r, -jawThickness / 2, z0), new Point3(lowLength, jawThickness, grip * 0.55), palette.FixtureJaw, angle, 0, 0);
            Box(jaws, new Point3(r, -jawThickness * 0.4, z0 + grip * 0.55), new Point3(lowLength * 0.5, jawThickness * 0.8, grip * 0.45),
                MillSolidRenderer.Lerp(palette.FixtureJaw, 0xFFFFFF, 0.18), angle, 0, 0);

            // Furo da chave na lateral (um bloco escuro encostado no corpo), entre as castanhas
            double keyAngle = angle + 60 * Math.PI / 180;
            double zKey = z0 - bodyLength * 0.55 - socket;
            Box(body, new Point3(bodyRadius - 0.6, -socket, zKey), new Point3(2.2, socket * 2, socket * 2),
                MillSolidRenderer.Lerp(palette.Fixture, 0x000000, 0.45), keyAngle, 0, 0);
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

    /// <summary>Medidas do porta-ferramenta BT40 (mm): flange, cone 7:24 e puxador.</summary>
    public const double BtFlangeRadius = 31.5, BtFlangeThickness = 15.9, BtGaugeRadius = 22.225, BtTaperLength = 62, BtStudLength = 16;

    /// <summary>Quanto a haste entra na porca (escondido dentro dela).</summary>
    public const double ToolEmbed = 6;

    /// <summary>Haste que aparece entre o fim da parte cortante e a face da porca.</summary>
    public static double ToolShankLength(double diameter) => Math.Clamp(diameter * 0.5, 4, 12);

    private static bool IsCoated(SetupTool? tool) =>
        tool is { Kind: ToolKind.FlatEndMill or ToolKind.BallEndMill or ToolKind.BullNoseEndMill or ToolKind.Drill };

    /// <summary>
    /// Parte cortante e porta-ferramenta, com a ponta na origem e o eixo em +Z. Sem cadastro no
    /// setup, usa comprimentos padrão proporcionais ao diâmetro. A fresa e a broca do setup saem
    /// com a cor da cobertura (tipo TiN) na parte cortante e aço na haste; o porta-ferramenta é um BT40
    /// com porca ER (diâmetro e comprimento do setup), flange com ranhura em V, cone 7:24 e puxador.
    /// </summary>
    public static (SolidMesh Cutter, SolidMesh Holder) MillTool(ToolProfile profile, SetupTool? tool, ScenePalette palette)
    {
        double r = profile.Radius;
        double tipHeight = profile.HeightAt(r);
        double cutting = Math.Max(tool?.CuttingLength ?? Math.Max(20, profile.Diameter * 2.5), tipHeight + Math.Max(1, r * 0.2));
        double holderRadius = Math.Max((tool?.HolderDiameter ?? Math.Max(profile.Diameter * 1.8, 20)) / 2, r);
        double holderLength = tool?.HolderLength ?? 45;
        double shank = ToolShankLength(profile.Diameter);
        double nutFace = cutting + shank;

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

        // Parte cortante (cobertura) até "cutting"; depois a haste de aço, que entra na porca
        outline.Add((r, cutting));
        int flutes = outline.Count - 1;
        outline.Add((r, nutFace + ToolEmbed));
        outline.Add((0, nutFace + ToolEmbed));
        var cutterColors = new uint[outline.Count - 1];
        uint cutting0 = IsCoated(tool) ? palette.Coating : palette.Tool;
        for (int i = 0; i < cutterColors.Length; i++)
        {
            cutterColors[i] = i < flutes ? cutting0 : palette.Tool;
        }

        var cutter = SolidMesh.Create(RevolutionSegments * outline.Count);
        Revolve(cutter, outline, 0, 0, palette.Tool, cutterColors);

        return (cutter, BtHolder(nutFace, holderRadius, holderLength, r, palette));
    }

    // Porta-ferramenta BT40 com porca ER: do plano da face da porca (zb) para cima, numa revolução só
    private static SolidMesh BtHolder(double zb, double nutRadius, double holderLength, double toolRadius, ScenePalette palette)
    {
        double ln = Math.Min(holderLength * 0.45, Math.Clamp(nutRadius * 1.3, 12, 42));
        ln = Math.Clamp(ln, Math.Min(10, holderLength), holderLength);
        double rn = Math.Min(nutRadius, Math.Max(toolRadius + 1.5, nutRadius * 0.64));    // rosto da porca
        double bodyR = nutRadius * 0.88;
        double flangeR = Math.Max(BtFlangeRadius, nutRadius * 1.15);
        double neckTop = Math.Max(bodyR, Math.Min(flangeR * 0.78, bodyR * 1.55));
        double zFlange = zb + Math.Max(holderLength, ln + 4);
        double tf = BtFlangeThickness;
        double gauge = Math.Min(BtGaugeRadius, flangeR * 0.72);
        double zTop = zFlange + tf + BtTaperLength;
        double rTaper = gauge - BtTaperLength * 7 / 48;    // cone 7:24 (diâmetro diminui 7 a cada 24 de comprimento)
        double rStud = Math.Max(3, Math.Min(6.5, rTaper * 0.6));
        double rKnob = rStud * 1.3;

        List<(double R, double Z)> outline =
        [
            (0, zb),
            (rn, zb),                                  // 0: rosto da porca
            (nutRadius, zb + ln * 0.38),               // 1: nariz cônico
            (nutRadius, zb + ln),                      // 2: corpo da porca
            (bodyR, zb + ln),                          // 3: degrau
            (neckTop, zFlange),                        // 4: pescoço
            (flangeR, zFlange),                        // 5: face inferior do flange
            (flangeR, zFlange + tf * 0.30),            // 6: ranhura em V
            (flangeR - Math.Min(1.8, flangeR * 0.06), zFlange + tf * 0.42),
            (flangeR, zFlange + tf * 0.54),
            (flangeR, zFlange + tf),                   // topo do flange
            (gauge, zFlange + tf),
            (rTaper, zTop),                            // cone 7:24
            (rStud, zTop),
            (rStud, zTop + BtStudLength * 0.6),
            (rKnob, zTop + BtStudLength * 0.6),
            (rKnob, zTop + BtStudLength),
            (0, zTop + BtStudLength),
        ];

        uint steel = MillSolidRenderer.Lerp(palette.Holder, 0xFFFFFF, 0.10);
        uint[] colors =
        [
            palette.Nut, palette.Nut, palette.Nut, palette.Nut,   // porca
            palette.Holder, palette.Holder,                       // pescoço e face do flange
            palette.Holder, palette.Holder, palette.Holder, palette.Holder, palette.Holder,   // flange
            steel, steel,                                         // cone
            palette.Nut, palette.Nut, palette.Nut, palette.Nut, palette.Nut,   // puxador
        ];

        var holder = SolidMesh.Create(RevolutionSegments * outline.Count);
        Revolve(holder, outline, 0, 0, palette.Holder, colors);
        return holder;
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

        var insert = SolidMesh.Create(16);
        Prism(insert, [(0, 0), b, c, a], -thickness / 2, thickness / 2, palette.Tool);

        // Quebra-cavaco: platô menor na face de cima do inserto, na cor da cobertura
        (double X, double Z) center = (c.X / 2, c.Z / 2);
        (double X, double Z) Shrink((double X, double Z) p) => (center.X + (p.X - center.X) * 0.62, center.Z + (p.Z - center.Z) * 0.62);
        Prism(insert, [Shrink((0, 0)), Shrink(b), Shrink(c), Shrink(a)], thickness / 2, thickness / 2 + 0.7, palette.Coating);

        // Haste de 20 mm por baixo do inserto, saindo em +X a partir do meio dele
        double centerX = c.X / 2, centerZ = c.Z / 2;
        var holder = SolidMesh.Create(8);
        Box(holder, new Point3(centerX, -thickness / 2 - 10, centerZ - 10), new Point3(70, 10, 20), palette.Holder);
        return (insert.Transformed(LatheToWorld), holder.Transformed(LatheToWorld));
    }

    // ── Fixações ──

    /// <summary>
    /// Base e mordentes de uma fixação do setup (morsa, grampo ou placa de 3 castanhas), pelas medidas
    /// do setup. Os mordentes precisam do bruto para se posicionar. A morsa tem chapa com orelhas e
    /// parafusos, corpo e mordentes com placa de contato; o grampo tem prisioneiro, arruela e porca; a
    /// placa tem chanfro, rebaixo central, furos da chave e castanhas de dois níveis.
    /// </summary>
    public static (SolidMesh Base, SolidMesh Jaws) Fixture(SetupFixture f, SetupStock? stock, ScenePalette palette)
    {
        var body = SolidMesh.Create(48);
        var jaws = SolidMesh.Create(48);

        if (f.Kind == SetupFixtureKind.ThreeJawChuck)
        {
            double radius = f.Width / 2;
            double cx = f.X + radius, cy = f.Y + f.Depth / 2;
            double chamfer = Math.Min(radius * 0.06, f.Height * 0.2);
            double recess = radius * 0.34;
            uint dark = MillSolidRenderer.Lerp(palette.Fixture, 0x000000, 0.18);
            uint bright = MillSolidRenderer.Lerp(palette.Fixture, 0xFFFFFF, 0.10);
            double plate = f.Height * 0.25;
            Revolve(body,
            [
                (0, f.Z),
                (radius * 0.86, f.Z),
                (radius * 0.86, f.Z + plate),
                (radius, f.Z + plate),
                (radius, f.Z + f.Height - chamfer),
                (radius - chamfer, f.Z + f.Height),
                (recess, f.Z + f.Height),
                (0, f.Z + f.Height),
            ], cx, cy, palette.Fixture, [dark, dark, palette.Fixture, palette.Fixture, palette.Fixture, bright, palette.Fixture]);

            if (stock is null)
            {
                return (body, jaws);
            }

            double stockRadius = stock.Kind == SetupStockKind.Prism ? Math.Max(stock.Width, stock.Depth) / 2 : stock.Diameter / 2;
            double jawLength = Math.Max(f.JawThickness * 1.65, radius * 0.24);
            double jawWidth = Math.Max(f.JawThickness * 0.55, radius * 0.085);
            double jawHeight = Math.Min(f.JawThickness, stock.Height);
            double r = Math.Clamp(Math.Max(stockRadius + jawLength * 0.30, f.JawOpening / 2), 0, Math.Max(0, radius - jawLength / 2));
            double socket = Math.Clamp(radius * 0.06, 2.5, 8);
            for (int j = 0; j < 3; j++)
            {
                double angle = (j * 120 + f.Rotation) * Math.PI / 180;
                double x = cx + Math.Cos(angle) * r, y = cy + Math.Sin(angle) * r;

                // Castanha em dois níveis (base radial e ressalto de contato), alinhada com o raio
                Box(jaws, new Point3(x - jawLength / 2, y - jawWidth / 2, stock.Z), new Point3(jawLength, jawWidth, jawHeight * 0.55),
                    palette.FixtureJaw, angle, x, y);
                Box(jaws, new Point3(x - jawLength * 0.42, y - jawWidth * 0.40, stock.Z + jawHeight * 0.55),
                    new Point3(jawLength * 0.55, jawWidth * 0.80, jawHeight * 0.45), MillSolidRenderer.Lerp(palette.FixtureJaw, 0xFFFFFF, 0.18), angle, x, y);

                // Furo da chave na lateral do corpo, entre as castanhas
                double keyAngle = angle + 60 * Math.PI / 180;
                double zKey = f.Z + plate + (f.Height - plate) * 0.45 - socket;
                Box(body, new Point3(cx + radius - 0.6, cy - socket, zKey), new Point3(2.2, socket * 2, socket * 2),
                    MillSolidRenderer.Lerp(palette.Fixture, 0x000000, 0.45), keyAngle, cx, cy);
            }

            return (body, jaws);
        }

        if (f.Kind == SetupFixtureKind.Vise)
        {
            // Chapa da base (com as orelhas nas pontas), corpo mais estreito por cima e parafusos de fixação
            double plateHeight = f.Height * 0.38, ear = Math.Min(f.Depth * 0.09, 16);
            uint plateColor = MillSolidRenderer.Lerp(palette.Fixture, 0x000000, 0.15);
            Box(body, new Point3(f.X, f.Y, f.Z), new Point3(f.Width, f.Depth, plateHeight), plateColor);
            Box(body, new Point3(f.X, f.Y + ear, f.Z + plateHeight), new Point3(f.Width, f.Depth - 2 * ear, f.Height - plateHeight), palette.Fixture);
            double bolt = Math.Clamp(ear * 0.32, 2.5, 8);
            foreach (double bx in new[] { 0.22, 0.78 })
            {
                foreach (double by in new[] { f.Y + ear * 0.5, f.Y + f.Depth - ear * 0.5 })
                {
                    Cylinder(body, f.X + f.Width * bx, by, f.Z + plateHeight, f.Z + plateHeight + bolt * 0.8, bolt, palette.Nut);
                }
            }

            double jaw = Math.Min(f.JawThickness, f.Depth / 4);
            double opening = Math.Min(f.JawOpening, f.Depth - 2 * jaw);
            double margin = Math.Max(0, (f.Depth - opening - 2 * jaw) / 2);
            double z = f.Z + f.Height;
            double plateThickness = Math.Min(2.5, Math.Max(0.5, opening * 0.04));
            uint insert = MillSolidRenderer.Lerp(palette.FixtureJaw, 0xFFFFFF, 0.22);
            double yFixed = f.Y + margin, yMoving = f.Y + margin + jaw + opening;

            // Mordente fixo e móvel, cada um com a placa de contato (mais clara) na face de dentro
            Box(jaws, new Point3(f.X, yFixed, z), new Point3(f.Width, jaw, jaw), palette.FixtureJaw);
            Box(jaws, new Point3(f.X + f.Width * 0.04, yFixed + jaw, z), new Point3(f.Width * 0.92, plateThickness, jaw * 0.7), insert);
            Box(jaws, new Point3(f.X, yMoving, z), new Point3(f.Width, jaw, jaw), palette.FixtureJaw);
            Box(jaws, new Point3(f.X + f.Width * 0.04, yMoving - plateThickness, z), new Point3(f.Width * 0.92, plateThickness, jaw * 0.7), insert);
            return (body, jaws);
        }

        // Grampo: base, calço e uma tira por cima, com prisioneiro, arruela e porca
        Box(body, new Point3(f.X, f.Y, f.Z), new Point3(f.Width, f.Depth, f.Height), palette.Fixture);
        double strapX = f.X + f.Width * 0.2, strapY = f.Y + f.Depth * 0.2, strapW = f.Width * 0.6, strapD = f.Depth * 0.6;
        double topZ = f.Z + f.Height;
        Box(jaws, new Point3(strapX, strapY, topZ), new Point3(strapW, strapD, f.JawThickness), palette.FixtureJaw);
        double stud = Math.Clamp(Math.Min(strapW, strapD) * 0.12, 2.5, 8);
        double sx = strapX + strapW / 2, sy = strapY + strapD / 2, sz = topZ + f.JawThickness;
        Cylinder(jaws, sx, sy, sz, sz + 2.2, stud * 1.9, MillSolidRenderer.Lerp(palette.FixtureJaw, 0xFFFFFF, 0.25));   // arruela
        Cylinder(jaws, sx, sy, sz + 2.2, sz + 2.2 + stud * 1.6, stud * 1.5, palette.Nut);                                // porca
        Cylinder(jaws, sx, sy, sz + 2.2 + stud * 1.6, sz + 2.2 + stud * 2.4, stud * 0.8, palette.Nut);                    // ponta do prisioneiro
        return (body, jaws);
    }

    // ── Formas básicas ──

    /// <summary>
    /// Sólido de revolução em volta do eixo vertical que passa por (cx, cy): o contorno vai do eixo
    /// embaixo, no sentido anti-horário no plano (raio, z), até o eixo em cima. Com
    /// "segmentColors" (uma cor por trecho do contorno) o sólido sai em várias cores.
    /// </summary>
    internal static void Revolve(SolidMesh mesh, IReadOnlyList<(double R, double Z)> outline, double cx, double cy, uint color,
        IReadOnlyList<uint>? segmentColors = null)
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

            uint segmentColor = segmentColors is not null && i < segmentColors.Count ? segmentColors[i] : color;

            // Normal para fora no plano (raio, z) de um contorno anti-horário
            double nr = z1 - z0, nz = -(r1 - r0);
            for (int k = 0; k < n; k++)
            {
                double a0 = k * step, a1 = a0 + step, mid = a0 + step / 2;
                Point3 At(double r, double z, double a) => new(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z);
                mesh.AddQuad(At(r0, z0, a0), At(r0, z0, a1), At(r1, z1, a1), At(r1, z1, a0),
                    new Point3(nr * Math.Cos(mid), nr * Math.Sin(mid), nz), segmentColor);
            }
        }
    }

    /// <summary>Cilindro reto de eixo vertical em (cx, cy), de z0 a z1.</summary>
    internal static void Cylinder(SolidMesh mesh, double cx, double cy, double z0, double z1, double radius, uint color) =>
        Revolve(mesh, [(0, z0), (radius, z0), (radius, z1), (0, z1)], cx, cy, color);

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
