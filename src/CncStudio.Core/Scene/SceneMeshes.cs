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

        // Nervuras de pega no corpo da porca ER
        uint rib = MillSolidRenderer.Lerp(palette.Nut, 0x000000, 0.35);
        double ribStart = zb + ln * 0.50, ribStep = ln * 0.50 / 7;
        for (int k = 0; k < 6; k++)
        {
            double z0 = ribStart + k * ribStep;
            Revolve(holder, [(nutRadius, z0), (nutRadius + 0.5, z0), (nutRadius + 0.5, z0 + ribStep * 0.45), (nutRadius, z0 + ribStep * 0.45)], 0, 0, rib);
        }

        // Ranhuras de arrasto do flange BT (duas, opostas: 16,1 mm de largura)
        uint slot = MillSolidRenderer.Lerp(palette.Holder, 0x000000, 0.6);
        for (int k = 0; k < 2; k++)
        {
            Box(holder, new Point3(flangeR - 7, -8.05, zFlange + tf * 0.05), new Point3(7, 16.1, tf * 0.9), slot, k * Math.PI, 0, 0);
        }

        return holder;
    }

    // ── Ferramenta do torno ──

    /// <summary>
    /// Ferramenta do torno pelo tipo do setup (sem cadastro: torneamento externo): inserto e corpo
    /// de <see cref="LatheToolShape"/> com a ponta de corte na origem, no plano XZ (X = raio, Z = eixo).
    /// A broca é um cilindro com ponta cônica no eixo. Devolvido já em coordenadas da cena
    /// (<see cref="LatheToWorld"/>).
    /// </summary>
    public static (SolidMesh Insert, SolidMesh Holder) LatheTool(ScenePalette palette, SetupTool? tool = null)
    {
        if (tool is { Kind: ToolKind.Drill })
        {
            return LatheDrill(tool, palette);
        }

        LatheToolShape shape = LatheToolShape.Of(tool);
        ToolKind kind = LatheToolShape.KindOf(tool);
        double half = shape.Thickness / 2;
        uint steel = MillSolidRenderer.Lerp(palette.Holder, 0xFFFFFF, 0.42);
        uint dark = MillSolidRenderer.Lerp(palette.Holder, 0x000000, 0.55);
        uint carbide = MillSolidRenderer.Lerp(palette.Coating, 0x000000, 0.45);
        uint carbideTop = MillSolidRenderer.Lerp(palette.Coating, 0xFFFFFF, 0.08);

        // Corpo: cabo (com chanfros), barra redonda nas internas, sede escura sob o inserto
        var holder = SolidMesh.Create(160);
        bool first = true;
        foreach (LatheToolShape.HolderPart part in shape.Holders)
        {
            if (part.Round)
            {
                (double X, double Z) c = (part.Polygon.Average(p => p.X), 0);
                double z0 = part.Polygon.Min(p => p.Z), z1 = part.Polygon.Max(p => p.Z);
                Cylinder(holder, c.X, (part.Y0 + part.Y1) / 2, (part.Y1 - part.Y0) / 2, z0, z1, 1.2, steel);
            }
            else
            {
                BeveledPrism(holder, part.Polygon, part.Y0, part.Y1, kind is (ToolKind.PartExternal or ToolKind.PartInternal) ? 1.2 : 2.0, steel);
            }

            if (first && !part.Round && kind is not (ToolKind.PartExternal or ToolKind.PartInternal))
            {
                // Sede do inserto: chapa escura um pouco maior que o inserto, sobre o cabo
                Extrude(holder, Ring(Scale(shape.Insert, 1.16), part.Y1), new Point3(0, LatheToolShape.SeatHeight, 0), dark);
            }

            first = false;
        }

        if (shape.Holders.Count > 0 && shape.Holders[0].Round)
        {
            // Barra interna: sede escura no ponto de apoio do inserto
            Extrude(holder, Ring(Scale(shape.Insert, 1.12), -half - 0.8), new Point3(0, 0.8, 0), dark);
        }

        // Inserto: pastilha de metal duro (cor escura de cobertura), com chanfro e face de cima mais clara
        var insert = SolidMesh.Create(96);
        BeveledPrism(insert, shape.Insert, -half, half, Math.Min(0.5, half * 0.3), carbide);
        Extrude(insert, Ring(Scale(shape.Insert, 0.9), half), new Point3(0, 0.06, 0), carbideTop);

        double cx = shape.Insert.Average(p => p.X), cz = shape.Insert.Average(p => p.Z);
        if (shape.ChipBreaker)
        {
            // Quebra-cavaco: platô elevado no meio do inserto e furo central com o parafuso de fixação
            Extrude(insert, Ring(Scale(shape.Insert, 0.62), half), new Point3(0, 0.7, 0), palette.Coating);
            if (kind == ToolKind.TurnExternal)
            {
                // Grampo superior sobre a metade de trás do inserto, com o parafuso de aperto
                List<(double X, double Z)> clamp = [(8.8, 9.0), (15.2, 8.0), (16.6, 15.8), (10.4, 16.8)];
                uint clampColor = MillSolidRenderer.Lerp(steel, 0xFFFFFF, 0.15);
                // Garra em cunha: a face de cima é menor e mais alta atrás, como o grampo de um PCLNR
                List<Point3> clampBase = Ring(clamp, half + 0.7), clampTop = Ring(Scale(clamp, 0.72), half + 4.2);
                Cap(holder, clampBase, new Point3(0, -1, 0), clampColor);
                Loft(holder, clampBase, clampTop, clampColor);
                Cap(holder, clampTop, new Point3(0, 1, 0), MillSolidRenderer.Lerp(clampColor, 0xFFFFFF, 0.1));
                // Bico da garra, apoiado na borda do inserto
                BeveledPrism(holder, [(6.9, 10.3), (9.2, 9.0), (10.6, 16.0), (8.6, 17.0)], half + 0.7, half + 2.2, 0.3, clampColor);
                Screw(holder, clamp.Average(p => p.X), half + 4.2, clamp.Average(p => p.Z), 2.6, 1.0, dark);
                Screw(holder, clamp.Average(p => p.X), half + 5.2, clamp.Average(p => p.Z), 1.5, 0.3, steel);
            }
            else
            {
                Screw(insert, cx, half + 0.7, cz, 2.2, 1.0, dark);
                Screw(insert, cx, half + 1.7, cz, 1.4, 0.4, steel);
            }
        }

        if (kind == ToolKind.PartExternal)
        {
            // Parafuso de fixação (cabeça redonda) no topo do bloco
            (double px, double pz, double ptop) = LatheToolShape.PartingBlockTop(Math.Max(0.5, tool?.InsertWidth ?? SetupTool.DefaultInsertWidth));
            Screw(holder, px, ptop, pz, 5.5, 1.2, dark);
            Screw(holder, px, ptop + 1.2, pz, 3.2, 0.4, steel);
        }

        return (insert.Transformed(LatheToWorld), holder.Transformed(LatheToWorld));
    }

    // ── Primitivas da ferramenta do torno (coordenadas do torno: X raio, Y altura, Z eixo) ──

    private static List<Point3> Ring(IReadOnlyList<(double X, double Z)> polygon, double y) =>
        [.. polygon.Select(p => new Point3(p.X, y, p.Z))];

    private static List<(double X, double Z)> Scale(IReadOnlyList<(double X, double Z)> polygon, double factor)
    {
        double cx = polygon.Average(p => p.X), cz = polygon.Average(p => p.Z);
        return [.. polygon.Select(p => (cx + (p.X - cx) * factor, cz + (p.Z - cz) * factor))];
    }

    private static Point3 Centroid(IReadOnlyList<Point3> ring) =>
        new(ring.Average(p => p.X), ring.Average(p => p.Y), ring.Average(p => p.Z));

    /// <summary>Tampa plana de um anel convexo, com a normal para o lado "outward".</summary>
    private static void Cap(SolidMesh mesh, IReadOnlyList<Point3> ring, Point3 outward, uint color)
    {
        for (int i = 1; i + 1 < ring.Count; i++)
        {
            mesh.AddQuad(ring[0], ring[i], ring[i + 1], ring[i + 1], outward, color);
        }
    }

    /// <summary>Faces laterais entre dois anéis com o mesmo número de pontos (normal para fora do centro).</summary>
    private static void Loft(SolidMesh mesh, IReadOnlyList<Point3> a, IReadOnlyList<Point3> b, uint color)
    {
        Point3 center = Centroid([.. a, .. b]);
        for (int i = 0; i < a.Count; i++)
        {
            int k = (i + 1) % a.Count;
            Point3 mid = Point3.Lerp(Point3.Lerp(a[i], a[k], 0.5), Point3.Lerp(b[i], b[k], 0.5), 0.5);
            Point3 normal = ViewCamera.Cross(a[k] - a[i], b[i] - a[i]);
            if (ViewCamera.Dot(normal, mid - center) < 0)
            {
                normal = new Point3(-normal.X, -normal.Y, -normal.Z);
            }

            mesh.AddQuad(a[i], a[k], b[k], b[i], normal, color);
        }
    }

    private static Point3 Dir(Point3 v) => ViewCamera.Normalize(v);

    /// <summary>Sólido de seção convexa: o anel andando "delta".</summary>
    private static void Extrude(SolidMesh mesh, IReadOnlyList<Point3> ring, Point3 delta, uint color)
    {
        List<Point3> moved = [.. ring.Select(p => p + delta)];
        Point3 up = Dir(delta);
        Cap(mesh, ring, new Point3(-up.X, -up.Y, -up.Z), color);
        Cap(mesh, moved, up, color);
        Loft(mesh, ring, moved, color);
    }

    /// <summary>Prisma vertical (Y) com os cantos de cima e de baixo chanfrados.</summary>
    private static void BeveledPrism(SolidMesh mesh, IReadOnlyList<(double X, double Z)> polygon, double y0, double y1, double bevel, uint color)
    {
        double radius = polygon.Average(p => Math.Sqrt(Math.Pow(p.X - polygon.Average(q => q.X), 2) + Math.Pow(p.Z - polygon.Average(q => q.Z), 2)));
        bevel = Math.Min(bevel, (y1 - y0) / 3);
        List<(double X, double Z)> inset = Scale(polygon, Math.Max(0.5, 1 - bevel / Math.Max(radius, 1e-6)));
        uint top = MillSolidRenderer.Lerp(color, 0xFFFFFF, 0.12);
        List<Point3> low = Ring(inset, y0), lowFull = Ring(polygon, y0 + bevel), highFull = Ring(polygon, y1 - bevel), high = Ring(inset, y1);
        Cap(mesh, low, new Point3(0, -1, 0), color);
        Loft(mesh, low, lowFull, color);
        Loft(mesh, lowFull, highFull, color);
        Loft(mesh, highFull, high, top);
        Cap(mesh, high, new Point3(0, 1, 0), top);
    }

    /// <summary>
    /// Cilindro ao longo de Z, com centro (cx, cy) e raio "radius", de z0 a z1, com chanfro nas pontas.
    /// </summary>
    private static void Cylinder(SolidMesh mesh, double cx, double cy, double radius, double z0, double z1, double bevel, uint color)
    {
        const int n = 20;
        List<Point3> Circle(double z, double r) =>
            [.. Enumerable.Range(0, n).Select(i => new Point3(cx + r * Math.Cos(2 * Math.PI * i / n), cy + r * Math.Sin(2 * Math.PI * i / n), z))];
        List<Point3> a = Circle(z0, radius - bevel), b = Circle(z0 + bevel, radius), c = Circle(z1 - bevel, radius), d = Circle(z1, radius - bevel);
        Cap(mesh, a, new Point3(0, 0, -1), color);
        Loft(mesh, a, b, color);
        Loft(mesh, b, c, color);
        Loft(mesh, c, d, MillSolidRenderer.Lerp(color, 0xFFFFFF, 0.1));
        Cap(mesh, d, new Point3(0, 0, 1), color);
    }

    /// <summary>Cilindro vertical (eixo Y) de raio "radius" em (cx, cz), de "y" até "y + height" (cabeça de parafuso).</summary>
    private static void Screw(SolidMesh mesh, double cx, double y, double cz, double radius, double height, uint color)
    {
        const int n = 16;
        List<(double X, double Z)> poly = [.. Enumerable.Range(0, n).Select(i => (cx + radius * Math.Cos(2 * Math.PI * i / n), cz + radius * Math.Sin(2 * Math.PI * i / n)))];
        Extrude(mesh, Ring(poly, y), new Point3(0, Math.Max(0.01, height), 0), color);
    }

    // Broca no eixo do torno: parte cortante com a cor da cobertura e haste de aço; ponta na origem, corpo em +Z
    private static (SolidMesh Cutter, SolidMesh Holder) LatheDrill(SetupTool tool, ScenePalette palette)
    {
        double r = Math.Max(0.5, tool.Diameter / 2);
        double tip = r / Math.Tan(Math.Clamp(tool.TipAngle, 20, 170) / 2 * Math.PI / 180);
        double flutes = Math.Max(tool.CuttingLength, tip + 1);
        var cutter = SolidMesh.Create(RevolutionSegments * 3);
        Revolve(cutter, [(0, 0), (r, tip), (r, flutes)], 0, 0, palette.Coating);
        var holder = SolidMesh.Create(RevolutionSegments * 3);
        double shank = Math.Max(r, tool.HolderDiameter / 2 * 0.6);
        Revolve(holder, [(r, flutes), (shank, flutes), (shank, flutes + Math.Max(30, tool.HolderLength))], 0, 0, palette.Holder);
        return (cutter.Transformed(LatheToWorld), holder.Transformed(LatheToWorld));
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
