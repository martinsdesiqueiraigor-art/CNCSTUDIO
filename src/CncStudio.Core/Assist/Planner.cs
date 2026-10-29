using System.Globalization;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Assist;

/// <summary>Ferramenta da biblioteca vista pelo planejador (o Core não conhece o cadastro do Code).</summary>
public sealed record PlanTool(int Number, double Diameter, bool IsDrill, string Name = "", int Teeth = 4);

/// <summary>Dados do plano automático 2D. <see cref="Thickness"/>: espessura do bruto (topo da peça em Z0).</summary>
public sealed record PlanInput(IReadOnlyList<Contour> Contours, double Thickness, MaterialEntry? Material, IReadOnlyList<PlanTool> Tools, double MaxHoleDrillDiameter = 20);

/// <summary>Sugestão de plano: operações para o Trabalho e avisos. É só uma sugestão: o usinador revisa, simula e roda no ar.</summary>
public sealed record PlanResult(IReadOnlyList<CamOperationSpec> Operations, IReadOnlyList<string> Notes)
{
    public bool Ok => Operations.Count > 0;

    /// <summary>Dados em que o plano se baseou. Serve para recusar a aplicação se algo mudou depois (<see cref="PlanValidity"/>).</summary>
    public PlanStamp? Stamp { get; init; }

    /// <summary>Impressão digital das operações no momento da sugestão (<see cref="PlanStamp.OfOperations"/>).</summary>
    public string? OperationsStamp { get; init; }
}

/// <summary>
/// Plano automático 2D a partir do desenho: contorno maior = peça; círculos pequenos dentro = furos; outros contornos
/// dentro = bolsões; contornos dentro de um bolsão viram ilhas. Só sugere o que o CAM sabe gerar (cada operação é testada).
/// Profundidades são suposições (o 2D não diz): furos e bolsões até a espessura/metade, contorno até a espessura.
/// </summary>
public static class AssistPlanner
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(double v) => v.ToString("0.##", Br);

    private static bool IsCircle(Contour c) => c.Segments.Count == 1 && c.Segments[0].IsArc && c.Segments[0].Start.DistanceTo(c.Segments[0].End) < 1e-6;

    public static PlanResult Plan(PlanInput input) => Seal(PlanCore(input), input);

    private static PlanResult Seal(PlanResult plan, PlanInput input)
    {
        // Cópia própria das operações: quem recebeu o plano não consegue alterar a lista por baixo do registro
        IReadOnlyList<CamOperationSpec> ops = Array.AsReadOnly(plan.Operations.ToArray());
        return plan with { Operations = ops, Stamp = PlanStamp.Of(input), OperationsStamp = PlanStamp.OfOperations(ops) };
    }

    private static PlanResult PlanCore(PlanInput input)
    {
        var notes = new List<string>();
        var ops = new List<CamOperationSpec>();
        List<Contour> closed = input.Contours.Where(c => c.IsClosed).ToList();
        if (closed.Count == 0)
        {
            return new PlanResult(ops, ["Não há contorno fechado no desenho."]);
        }

        if (!(input.Thickness > 0))
        {
            return new PlanResult(ops, ["Informe a espessura do bruto."]);
        }

        Contour outer = closed.OrderByDescending(c => Math.Abs(c.SignedArea)).First();
        List<Contour> inside = CamOperations.FindIslands(outer, closed).ToList();
        int ignored = closed.Count - 1 - inside.Count;
        if (ignored > 0)
        {
            notes.Add($"{ignored} contorno(s) fora do contorno principal foram ignorados.");
        }

        // Cada contorno interno que está dentro de outro vira ilha dele, não operação própria
        var nested = new HashSet<Contour>();
        foreach (Contour c in inside)
        {
            foreach (Contour i in CamOperations.FindIslands(c, inside))
            {
                nested.Add(i);
            }
        }

        List<Contour> top = inside.Where(c => !nested.Contains(c)).ToList();
        var holes = top.Where(c => IsCircle(c) && c.Segments[0].Radius * 2 <= input.MaxHoleDrillDiameter && CamOperations.FindIslands(c, inside).Count == 0).ToList();
        var pockets = top.Except(holes).OrderBy(c => Math.Abs(c.SignedArea)).ToList();

        int nextNumber = Math.Max(0, input.Tools.Select(t => t.Number).DefaultIfEmpty(0).Max()) + 1;
        var used = new Dictionary<string, CamTool>();
        CamTool Pick(string key, double diameter, bool drill, string fallbackName)
        {
            if (used.TryGetValue(key, out CamTool? known))
            {
                return known;
            }

            PlanTool? found = input.Tools.Where(t => t.IsDrill == drill && (drill ? Math.Abs(t.Diameter - diameter) < 0.05 : t.Diameter <= diameter + 1e-9))
                .OrderByDescending(t => t.Diameter).FirstOrDefault();
            CamTool tool = found is not null ? ToolFor(found.Number, found.Diameter, found.Name, found.Teeth, drill) : ToolFor(nextNumber++, diameter, fallbackName, drill ? 2 : 4, drill);
            used[key] = tool;
            return tool;
        }

        CamTool ToolFor(int number, double diameter, string name, int teeth, bool drill)
        {
            MaterialEntry? m = input.Material;
            (double Rpm, double Feed)? s = m?.SuggestMill(diameter, teeth);
            double rpm = s?.Rpm ?? 3000, feed = s?.Feed ?? 500;
            if (drill)
            {
                feed = m is null ? 120 : Math.Max(30, Math.Round(feed / 4));
            }

            return new CamTool(number, diameter, rpm, feed, Math.Round(feed * 0.4), name);
        }

        CamLevels LevelsFor(double depth, CamTool tool)
        {
            double step = input.Material?.SuggestDepth(tool.Diameter) is > 0 and double d ? d : tool.Diameter * 0.5;
            return new CamLevels(50, 2, 0, -depth, Math.Min(Math.Max(step, 0.1), depth));
        }

        double depthHole = input.Thickness;
        double depthPocket = Math.Round(input.Thickness / 2, 2);

        // 1. Furos (mesma broca para o mesmo Ø; furos de Ø diferentes viram uma operação cada)
        foreach (IGrouping<double, Contour> g in holes.GroupBy(c => Math.Round(c.Segments[0].Radius * 2, 2)).OrderBy(g => g.Key))
        {
            CamTool drill = Pick("d" + N(g.Key), g.Key, true, "Broca Ø" + N(g.Key));
            foreach (Contour c in g)
            {
                CamOperationSpec spec = new(JobOpKind.Drilling, drill, new CamLevels(50, 2, 0, -depthHole, 5)) { Drill = new DrillSpec(DrillCycleKind.Peck, depthHole > 3 * g.Key ? Math.Round(g.Key, 1) : 0, 0), ContourIndex = input.Contours.ToList().IndexOf(c), Label = $"Furo Ø{N(g.Key)}" };
                if (spec.Drill!.Peck == 0)
                {
                    spec = spec with { Drill = new DrillSpec(DrillCycleKind.Drill, 0, 0) };
                }

                ops.Add(spec);
            }
        }

        // 2. Bolsões (testa da maior ferramenta para a menor até o CAM aceitar)
        int pocketNo = 0;
        foreach (Contour c in pockets)
        {
            pocketNo++;
            IReadOnlyList<Contour> islands = CamOperations.FindIslands(c, inside);
            (double x0, double y0, double x1, double y1) = c.BoundsXY();
            double limit = Math.Min(Math.Min(x1 - x0, y1 - y0) / 2, 20);
            CamOperationSpec? chosen = null;
            string failure = "nenhuma fresa da biblioteca cabe";
            foreach (double d in Candidates(input.Tools, limit))
            {
                CamTool tool = Pick("m" + N(d), d, false, "Fresa Ø" + N(d));
                CamLevels levels = LevelsFor(depthPocket, tool);
                var p = new PocketParams(60, 0.1, Math.Min(2, tool.Radius));
                OperationResult r = islands.Count > 0 ? CamOperations.PocketWithIslands(c, islands, tool, levels, p) : CamOperations.Pocket(c, tool, levels, p);
                if (r.Ok)
                {
                    chosen = new CamOperationSpec(JobOpKind.Pocket, tool, levels) { ContourIndex = input.Contours.ToList().IndexOf(c), PocketParams = p, Islands = islands.Count > 0, Label = $"Bolsão {pocketNo}" };
                    break;
                }

                failure = r.Error ?? failure;
            }

            if (chosen is null)
            {
                notes.Add($"Bolsão {pocketNo} ({N(x1 - x0)} × {N(y1 - y0)}): sem plano ({failure}).");
            }
            else
            {
                ops.Add(chosen);
            }
        }

        // 3. Contorno da peça por último
        (double ox0, double oy0, double ox1, double oy1) = outer.BoundsXY();
        bool outerOk = false;
        foreach (double d in Candidates(input.Tools, 12))
        {
            CamTool tool = Pick("m" + N(d), d, false, "Fresa Ø" + N(d));
            CamLevels levels = LevelsFor(input.Thickness, tool);
            var p = new ContourParams(ContourSide.Outside, Climb: true, ControllerComp: false, Allowance: 0, LeadRadius: Math.Min(3, Math.Max(tool.Radius + 0.5, 1)));
            if (CamOperations.Contour(outer, tool, levels, p).Ok)
            {
                ops.Add(new CamOperationSpec(JobOpKind.Contour, tool, levels) { ContourIndex = input.Contours.ToList().IndexOf(outer), ContourParams = p, Label = "Contorno da peça" });
                outerOk = true;
                break;
            }
        }

        if (!outerOk)
        {
            notes.Add("Contorno da peça: nenhuma fresa da biblioteca serve.");
        }

        notes.Add($"Peça de {N(ox1 - ox0)} × {N(oy1 - oy0)} mm, espessura {N(input.Thickness)} mm (suposta: topo em Z0).");
        notes.Add("Profundidades supostas: furos e contorno até a espessura, bolsões até a metade. Ajuste no Trabalho. Para passar do fundo, use calço ou mesa de sacrifício.");
        notes.Add("Contorno da peça por último, sem pontes: a peça solta no fim. Prenda bem e simule antes. Primeira execução no ar.");
        if (input.Material is { Draft: true } mat)
        {
            notes.Add($"Dados de corte de \"{mat.Name}\" ainda são rascunho: confira no catálogo da ferramenta.");
        }

        return new PlanResult(ops, notes);
    }

    /// <summary>Diâmetros de fresa a tentar, do maior ao menor: os da biblioteca até o limite; sem biblioteca, uma lista padrão.</summary>
    private static IEnumerable<double> Candidates(IReadOnlyList<PlanTool> tools, double limit)
    {
        List<double> fromBank = tools.Where(t => !t.IsDrill && t.Diameter <= limit + 1e-9).Select(t => t.Diameter).Distinct().OrderByDescending(d => d).ToList();
        if (fromBank.Count > 0)
        {
            return fromBank;
        }

        return new[] { 20.0, 16, 12, 10, 8, 6, 5, 4, 3, 2 }.Where(d => d <= limit + 1e-9);
    }
}
