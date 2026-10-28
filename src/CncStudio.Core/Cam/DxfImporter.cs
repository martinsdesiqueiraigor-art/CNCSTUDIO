using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>Unidade de desenho declarada no cabeçalho do DXF (<c>$INSUNITS</c>).</summary>
public enum DxfUnits { Unspecified, Inches, Feet, Millimeters, Centimeters, Meters, Other }

/// <summary>Resultado da leitura de um DXF: contornos fechados, cadeias abertas, unidade declarada e avisos em português.</summary>
public sealed record DxfImportResult(IReadOnlyList<Contour> Contours, IReadOnlyList<IReadOnlyList<Segment2>> OpenChains, IReadOnlyList<string> Warnings, DxfUnits Units = DxfUnits.Unspecified)
{
    /// <summary>Fator para converter a unidade do arquivo em milímetros (1 quando não informada ou desconhecida).</summary>
    public double ToMillimeters => Units switch
    {
        DxfUnits.Inches => 25.4,
        DxfUnits.Feet => 304.8,
        DxfUnits.Centimeters => 10,
        DxfUnits.Meters => 1000,
        _ => 1,
    };

    /// <summary>Nome da unidade em português, para mostrar ao usuário.</summary>
    public string UnitsName => Units switch
    {
        DxfUnits.Inches => "polegadas",
        DxfUnits.Feet => "pés",
        DxfUnits.Millimeters => "milímetros",
        DxfUnits.Centimeters => "centímetros",
        DxfUnits.Meters => "metros",
        DxfUnits.Other => "uma unidade que o Casillas não converte",
        _ => "não informada",
    };

    /// <summary>Mesmo desenho com todas as medidas multiplicadas por <paramref name="factor"/> (a origem fica onde está).</summary>
    public DxfImportResult Scaled(double factor) => Math.Abs(factor - 1) < 1e-12
        ? this
        : this with
        {
            Contours = [.. Contours.Select(c => c.Scaled(factor))],
            OpenChains = [.. OpenChains.Select(ch => (IReadOnlyList<Segment2>)[.. ch.Select(sg => sg.Scaled(factor))])],
        };
}

/// <summary>
/// Lê DXF em texto (ASCII) e monta contornos fechados em 2D. Entende LINE, ARC, CIRCLE, LWPOLYLINE e POLYLINE (com bulge);
/// junta retas e arcos soltos que se encontram nas pontas. O resto (SPLINE, ELLIPSE, texto…) é ignorado com aviso.
/// </summary>
public static class DxfImporter
{
    private const double JoinTolerance = 1e-3;

    private sealed record Entity(string Type, List<(int Code, string Value)> Pairs);

    public static DxfImportResult Parse(string text)
    {
        var warnings = new List<string>();
        var closed = new List<Contour>();
        var loose = new List<Segment2>();
        var ignored = new SortedDictionary<string, int>();

        foreach (Entity e in ReadEntities(text))
        {
            switch (e.Type)
            {
                case "LINE":
                    {
                        var a = new Point2(Num(e, 10), Num(e, 20));
                        var b = new Point2(Num(e, 11), Num(e, 21));
                        if (a.DistanceTo(b) > 1e-9)
                        {
                            loose.Add(Segment2.Line(a, b));
                        }

                        break;
                    }

                case "ARC":
                    {
                        var c = new Point2(Num(e, 10), Num(e, 20));
                        double r = Num(e, 40);
                        double a0 = Num(e, 50) * Math.PI / 180;
                        double a1 = Num(e, 51) * Math.PI / 180;
                        if (r > 1e-9)
                        {
                            loose.Add(Segment2.Arc(OnCircle(c, r, a0), OnCircle(c, r, a1), c, ccw: true));
                        }

                        break;
                    }

                case "CIRCLE":
                    {
                        double r = Num(e, 40);
                        if (r > 1e-9)
                        {
                            closed.Add(Contour.Circle(new Point2(Num(e, 10), Num(e, 20)), r));
                        }

                        break;
                    }

                case "LWPOLYLINE":
                    AddPolyline(LightVertices(e), (IntVal(e, 70) & 1) == 1, closed, loose);
                    break;

                case "POLYLINE":
                    AddPolyline(e.Pairs.Count == 0 ? [] : PolyVertices(e), (IntVal(e, 70) & 1) == 1, closed, loose);
                    break;

                case "VERTEX":
                case "SEQEND":
                    break;

                default:
                    ignored[e.Type] = ignored.GetValueOrDefault(e.Type) + 1;
                    break;
            }
        }

        foreach (KeyValuePair<string, int> kv in ignored)
        {
            warnings.Add($"{kv.Value} × {kv.Key} ignorado(s): tipo não suportado.");
        }

        var open = new List<IReadOnlyList<Segment2>>();
        foreach (List<Segment2> chain in Chain(loose))
        {
            var contour = new Contour(chain);
            if (contour.IsClosed)
            {
                closed.Add(contour);
            }
            else
            {
                open.Add(chain);
            }
        }

        if (open.Count > 0)
        {
            warnings.Add($"{open.Count} cadeia(s) aberta(s): as pontas não se encontram (confira o desenho no CAD).");
        }

        if (closed.Count == 0 && open.Count == 0)
        {
            warnings.Add("Nenhum contorno encontrado no arquivo.");
        }

        DxfUnits units = ReadUnits(text);
        if (units == DxfUnits.Unspecified && (closed.Count > 0 || open.Count > 0))
        {
            warnings.Add("O DXF não informa a unidade: as medidas foram lidas como milímetros (confira o tamanho).");
        }
        else if (units == DxfUnits.Other)
        {
            warnings.Add("A unidade do DXF não é mm, polegada, pé, cm nem m: as medidas foram lidas como milímetros (confira o tamanho).");
        }

        return new DxfImportResult(closed, open, warnings, units);
    }

    /// <summary>
    /// Lê o DXF e já converte as medidas para milímetros pela unidade do cabeçalho. Avisa a conversão feita;
    /// sem unidade informada, mantém as medidas (e o aviso de <see cref="Parse"/>).
    /// </summary>
    public static DxfImportResult ParseInMillimeters(string text)
    {
        DxfImportResult r = Parse(text);
        double factor = r.ToMillimeters;
        if (Math.Abs(factor - 1) < 1e-12)
        {
            return r;
        }

        DxfImportResult scaled = r.Scaled(factor);
        return scaled with { Warnings = [$"Unidade do DXF: {r.UnitsName}. Medidas convertidas para milímetros (× {factor.ToString("0.####", CultureInfo.GetCultureInfo("pt-BR"))}).", .. r.Warnings] };
    }

    /// <summary>Lê <c>$INSUNITS</c> no cabeçalho (1 pol, 2 pé, 4 mm, 5 cm, 6 m; 0 ou ausente = não informada).</summary>
    private static DxfUnits ReadUnits(string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i + 3 < lines.Length; i++)
        {
            if (lines[i].Trim() == "9" && lines[i + 1].Trim().Equals("$INSUNITS", StringComparison.OrdinalIgnoreCase))
            {
                if (lines[i + 2].Trim() == "70" && int.TryParse(lines[i + 3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                {
                    return code switch
                    {
                        0 => DxfUnits.Unspecified,
                        1 => DxfUnits.Inches,
                        2 => DxfUnits.Feet,
                        4 => DxfUnits.Millimeters,
                        5 => DxfUnits.Centimeters,
                        6 => DxfUnits.Meters,
                        _ => DxfUnits.Other,
                    };
                }

                return DxfUnits.Unspecified;
            }

            if (lines[i].Trim() == "0" && lines[i + 1].Trim() == "ENDSEC")
            {
                break; // fim do cabeçalho
            }
        }

        return DxfUnits.Unspecified;
    }

    private static Point2 OnCircle(Point2 c, double r, double angle) =>
        new(c.X + (r * Math.Cos(angle)), c.Y + (r * Math.Sin(angle)));

    private static List<Entity> ReadEntities(string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var pairs = new List<(int, string)>();
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            if (int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                pairs.Add((code, lines[i + 1].Trim()));
            }
            else
            {
                i--; // linha fora de par: realinha
            }
        }

        var entities = new List<Entity>();
        bool inEntities = false;
        Entity? current = null;
        Entity? polyline = null;
        for (int i = 0; i < pairs.Count; i++)
        {
            (int code, string value) = pairs[i];
            if (code == 0 && value == "SECTION" && i + 1 < pairs.Count && pairs[i + 1] is (2, "ENTITIES"))
            {
                inEntities = true;
                i++;
                continue;
            }

            if (!inEntities)
            {
                continue;
            }

            if (code == 0 && value == "ENDSEC")
            {
                break;
            }

            if (code == 0 && value == "VERTEX" && polyline is not null)
            {
                polyline.Pairs.Add((-1, "V")); // marca o começo de um vértice do POLYLINE
                current = polyline;
            }
            else if (code == 0 && value == "SEQEND")
            {
                polyline = null;
                current = null;
            }
            else if (code == 0)
            {
                current = new Entity(value, []);
                entities.Add(current);
                polyline = value == "POLYLINE" ? current : null;
            }
            else
            {
                current?.Pairs.Add((code, value));
            }
        }

        return entities;
    }

    private static double Num(Entity e, int code)
    {
        foreach ((int c, string v) in e.Pairs)
        {
            if (c == code && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                return d;
            }
        }

        return 0;
    }

    private static int IntVal(Entity e, int code) => (int)Math.Round(Num(e, code));

    private readonly record struct Vertex(Point2 P, double Bulge);

    private static List<Vertex> LightVertices(Entity e)
    {
        var list = new List<Vertex>();
        double x = 0;
        bool haveX = false;
        foreach ((int code, string value) in e.Pairs)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                continue;
            }

            if (code == 10)
            {
                x = d;
                haveX = true;
            }
            else if (code == 20 && haveX)
            {
                list.Add(new Vertex(new Point2(x, d), 0));
                haveX = false;
            }
            else if (code == 42 && list.Count > 0)
            {
                list[^1] = list[^1] with { Bulge = d };
            }
        }

        return list;
    }

    /// <summary>POLYLINE antigo: os vértices vêm como entidades VERTEX; só vale o que está depois da primeira marca (o cabeçalho tem um 10/20 falso).</summary>
    private static List<Vertex> PolyVertices(Entity e)
    {
        int first = e.Pairs.FindIndex(p => p.Code == -1);
        return first < 0 ? [] : LightVertices(new Entity(e.Type, e.Pairs.GetRange(first, e.Pairs.Count - first)));
    }

    private static void AddPolyline(List<Vertex> v, bool isClosed, List<Contour> closed, List<Segment2> loose)
    {
        if (v.Count < 2)
        {
            return;
        }

        var segs = new List<Segment2>();
        int last = isClosed ? v.Count : v.Count - 1;
        for (int i = 0; i < last; i++)
        {
            Vertex a = v[i];
            Vertex b = v[(i + 1) % v.Count];
            if (a.P.DistanceTo(b.P) < 1e-9)
            {
                continue;
            }

            segs.Add(BulgeSegment(a.P, b.P, a.Bulge));
        }

        if (segs.Count == 0)
        {
            return;
        }

        if (isClosed)
        {
            closed.Add(new Contour(segs));
        }
        else
        {
            loose.AddRange(segs);
        }
    }

    /// <summary>Trecho entre dois vértices com "bulge" (tan de 1/4 do ângulo; positivo = anti-horário).</summary>
    private static Segment2 BulgeSegment(Point2 p0, Point2 p1, double bulge)
    {
        if (Math.Abs(bulge) < 1e-9)
        {
            return Segment2.Line(p0, p1);
        }

        double theta = 4 * Math.Atan(bulge);
        Point2 chord = p1 - p0;
        double half = chord.Length / 2;
        Point2 dir = chord.Normalized();
        var left = new Point2(-dir.Y, dir.X);
        Point2 mid = p0 + (chord * 0.5);
        Point2 center = mid + (left * (half / Math.Tan(theta / 2)));
        return Segment2.Arc(p0, p1, center, bulge > 0);
    }

    private static Segment2 Flip(Segment2 s) =>
        s.IsArc ? Segment2.Arc(s.End, s.Start, s.Center, !s.Ccw) : Segment2.Line(s.End, s.Start);

    /// <summary>Junta trechos soltos em cadeias pelas pontas (inverte o que for preciso).</summary>
    private static List<List<Segment2>> Chain(List<Segment2> segments)
    {
        var pool = new List<Segment2>(segments);
        var chains = new List<List<Segment2>>();
        while (pool.Count > 0)
        {
            var chain = new List<Segment2> { pool[0] };
            pool.RemoveAt(0);
            bool grew = true;
            while (grew && pool.Count > 0)
            {
                grew = false;
                Point2 tail = chain[^1].End;
                if (chain.Count > 1 || chain[0].Start.DistanceTo(chain[0].End) > JoinTolerance)
                {
                    if (tail.DistanceTo(chain[0].Start) < JoinTolerance)
                    {
                        break; // já fechou
                    }
                }

                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i].Start.DistanceTo(tail) < JoinTolerance)
                    {
                        chain.Add(pool[i] with { Start = tail });
                    }
                    else if (pool[i].End.DistanceTo(tail) < JoinTolerance)
                    {
                        chain.Add(Flip(pool[i]) with { Start = tail });
                    }
                    else
                    {
                        continue;
                    }

                    pool.RemoveAt(i);
                    grew = true;
                    break;
                }

                if (!grew)
                {
                    // tenta crescer pelo começo
                    Point2 head = chain[0].Start;
                    for (int i = 0; i < pool.Count; i++)
                    {
                        if (pool[i].End.DistanceTo(head) < JoinTolerance)
                        {
                            chain.Insert(0, pool[i] with { End = head });
                        }
                        else if (pool[i].Start.DistanceTo(head) < JoinTolerance)
                        {
                            chain.Insert(0, Flip(pool[i]) with { End = head });
                        }
                        else
                        {
                            continue;
                        }

                        pool.RemoveAt(i);
                        grew = true;
                        break;
                    }
                }
            }

            chains.Add(chain);
        }

        return chains;
    }
}
