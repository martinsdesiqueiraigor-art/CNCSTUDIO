using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using CncStudio.App.Settings;
using CncStudio.Core.Cam;
using CncStudio.Core.Setup;

namespace CncStudio.App.Modules;

/// <summary>Painel de propriedades da operação selecionada (J3): cada campo vira uma alteração do <see cref="CamOperationSpec"/>.</summary>
public partial class JobView
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string Fmt(double v) => v.ToString("0.###", Br);

    private static double ParseNum(string? text)
    {
        string t = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : double.NaN;
    }

    private void BuildProps()
    {
        PropsPanel.Children.Clear();
        if (SelectedOp() is not { } i || i >= _job.Operations.Count)
        {
            PropsPanel.Children.Add(new TextBlock { Text = "Propriedades", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
            PropsPanel.Children.Add(new TextBlock { Text = "Selecione uma operação na árvore para editar os dados dela.", Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
            return;
        }

        CamOperationSpec s = _job.Operations[i];
        PropsPanel.Children.Add(new TextBlock { Text = "Propriedades", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        PropsPanel.Children.Add(new TextBlock { Text = CamJobInfo.OpTitle(i, s), Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });

        Text(i, "Nome", s.Label, "padrão", (x, v) => x with { Label = v.Trim() });

        if (s.Kind is JobOpKind.Contour or JobOpKind.Pocket or JobOpKind.Facing or JobOpKind.Drilling)
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            bool drill = s.Kind == JobOpKind.Drilling;
            if (drill)
            {
                combo.Items.Add(new ComboBoxItem { Content = "Todos os círculos" });
            }

            for (int c = 0; c < _job.Contours.Count; c++)
            {
                combo.Items.Add(new ComboBoxItem { Content = CamJobInfo.ContourTitle(_job.Contours[c], c) });
            }

            int offset = drill ? 1 : 0;
            combo.SelectedIndex = Math.Clamp(s.ContourIndex + offset, 0, Math.Max(0, combo.Items.Count - 1));
            combo.SelectionChanged += (_, _) =>
            {
                int chosen = combo.SelectedIndex - offset;
                if (chosen != s.ContourIndex)
                {
                    Edit(i, x => x with { ContourIndex = chosen }, rebuild: true);
                }
            };
            Section("Desenho");
            PropsPanel.Children.Add(combo);
        }

        Section("Ferramenta");
        BankPicker(i);
        Text(i, "Nome da ferramenta", s.Tool.Name, "ex.: Fresa topo Z4", (x, v) => x with { Tool = x.Tool with { Name = v.Trim() } });
        Num(i, "Ferramenta (T)", "", s.Tool.Number, (x, v) => x with { Tool = x.Tool with { Number = (int)Math.Round(v) } });
        Num(i, "Diâmetro", "mm", s.Tool.Diameter, (x, v) => x with { Tool = x.Tool with { Diameter = v } });
        Section("Condição de corte");
        Num(i, "Rotação", "rpm", s.Tool.SpindleRpm, (x, v) => x with { Tool = x.Tool with { SpindleRpm = v } });
        Num(i, s.Kind == JobOpKind.WrapText ? "Avanço na superfície" : "Avanço no plano", "mm/min", s.Tool.FeedXY, (x, v) => x with { Tool = x.Tool with { FeedXY = v } });
        Num(i, "Avanço de descida", "mm/min", s.Tool.FeedZ, (x, v) => x with { Tool = x.Tool with { FeedZ = v } });

        if (s.Kind != JobOpKind.WrapText)
        {
            Section("Alturas (Z)");
            Num(i, "Z seguro (rápido)", "mm", s.Levels.SafeZ, (x, v) => x with { Levels = x.Levels with { SafeZ = v } });
            Num(i, "Z de aproximação", "mm", s.Levels.ClearanceZ, (x, v) => x with { Levels = x.Levels with { ClearanceZ = v } });
            Num(i, "Z topo da peça", "mm", s.Levels.TopZ, (x, v) => x with { Levels = x.Levels with { TopZ = v } });
            Num(i, "Z fundo", "mm", s.Levels.BottomZ, (x, v) => x with { Levels = x.Levels with { BottomZ = v } });
            Num(i, "Passe em Z", "mm", s.Levels.StepDown, (x, v) => x with { Levels = x.Levels with { StepDown = v } });
        }

        switch (s.Kind)
        {
            case JobOpKind.Contour when s.ContourParams is { } c:
                Section("Contorno");
                Pick(i, "Lado", ["Por fora", "Por dentro", "Sobre a linha"], (int)c.Side, (x, n) => x with { ContourParams = c with { Side = (ContourSide)n } }, rebuild: true);
                Check(i, "Concordante (climb)", c.Climb, (x, b) => x with { ContourParams = c with { Climb = b } });
                Check(i, "Compensação no controle (G41/G42)", c.ControllerComp, (x, b) => x with { ContourParams = c with { ControllerComp = b } });
                Num(i, "Sobremetal", "mm", c.Allowance, (x, v) => x with { ContourParams = c with { Allowance = v } });
                Num(i, "Raio de entrada", "mm", c.LeadRadius, (x, v) => x with { ContourParams = c with { LeadRadius = v } });
                break;
            case JobOpKind.Pocket when s.PocketParams is { } p:
                Section("Bolsão");
                Num(i, "Passo lateral", "% Ø", p.StepOverPercent, (x, v) => x with { PocketParams = p with { StepOverPercent = v } });
                Num(i, "Sobremetal de acabamento", "mm", p.FinishAllowance, (x, v) => x with { PocketParams = p with { FinishAllowance = v } });
                Num(i, "Raio de entrada", "mm", p.LeadRadius, (x, v) => x with { PocketParams = p with { LeadRadius = v } });
                break;
            case JobOpKind.Facing:
                Section("Faceamento");
                Num(i, "Passo lateral", "% Ø", s.FacingStepOverPercent, (x, v) => x with { FacingStepOverPercent = v });
                break;
            case JobOpKind.Drilling when s.Drill is { } d:
                Section("Furação");
                Pick(i, "Ciclo", ["G81 furar", "G82 com pausa", "G83 bicando", "G73 quebra-cavaco"], (int)d.Cycle, (x, n) => x with { Drill = d with { Cycle = (DrillCycleKind)n } });
                Num(i, "Bicada (Q)", "mm", d.Peck, (x, v) => x with { Drill = d with { Peck = v } });
                Num(i, "Pausa", "s", d.DwellSeconds, (x, v) => x with { Drill = d with { DwellSeconds = v } });
                break;
            case JobOpKind.WrapText when s.Wrap is { } w:
                Section("Texto no cilindro (4º eixo contínuo)");
                Text(i, "Texto", w.Text, "", (x, v) => x with { Wrap = w with { Text = v } });
                Num(i, "Altura das letras", "mm", w.LetterHeight, (x, v) => x with { Wrap = w with { LetterHeight = v } });
                Pick(i, "Eixo rotativo", ["A (gira em torno de X)", "B (gira em torno de Y)"], w.Axis == "B" ? 1 : 0, (x, n) => x with { Wrap = w with { Axis = n == 1 ? "B" : "A" } });
                Num(i, "Diâmetro da peça", "mm", w.Setup.Diameter, (x, v) => x with { Wrap = w with { Setup = w.Setup with { Diameter = v } } });
                Num(i, "Início ao longo do eixo", "mm", w.Setup.StartAxial, (x, v) => x with { Wrap = w with { Setup = w.Setup with { StartAxial = v } } });
                Num(i, "Ângulo inicial", "°", w.Setup.StartAngle, (x, v) => x with { Wrap = w with { Setup = w.Setup with { StartAngle = v } } });
                Num(i, "Profundidade", "mm", w.Setup.Depth, (x, v) => x with { Wrap = w with { Setup = w.Setup with { Depth = v } } });
                Num(i, "Passe em profundidade", "mm", w.Setup.PassDepth, (x, v) => x with { Wrap = w with { Setup = w.Setup with { PassDepth = v } } });
                Num(i, "Folga acima da peça", "mm", w.Setup.Clearance, (x, v) => x with { Wrap = w with { Setup = w.Setup with { Clearance = v } } });
                Num(i, "Avanço de mergulho", "mm/min", w.Setup.PlungeFeed, (x, v) => x with { Wrap = w with { Setup = w.Setup with { PlungeFeed = v } } });
                break;
        }

        if (s.Kind != JobOpKind.WrapText)
        {
            Section("4º eixo");
            Check(i, "Face girada (perfil da máquina)", s.Face is not null, (x, b) => x with { Face = b ? new RotaryFaceSpec("A", 90, 25) : null }, rebuild: true);
            if (s.Face is { } f)
            {
                Pick(i, "Eixo", ["A", "B"], f.Axis == "B" ? 1 : 0, (x, n) => x with { Face = f with { Axis = n == 1 ? "B" : "A" } });
                Num(i, "Ângulo da face", "°", f.Angle, (x, v) => x with { Face = f with { Angle = v } });
                Num(i, "Do centro do eixo até a face", "mm", f.AxisToFace, (x, v) => x with { Face = f with { AxisToFace = v } });
                PropsPanel.Children.Add(new TextBlock { Text = "Desenhe a face de cima (Z 0 na face). Rode primeiro no ar e confira o sentido de A/B.", Opacity = 0.65, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            }
        }
    }

    // ---------------------------------------------------------------- campos

    private void Section(string title) =>
        PropsPanel.Children.Add(new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Avalonia.Thickness(0, 8, 0, 0) });

    private static Grid Row(string label, Control field, string unit = "")
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,52"), ColumnSpacing = 6 };
        g.Children.Add(new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        Grid.SetColumn(field, 1);
        g.Children.Add(field);
        var u = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65, FontSize = 11 };
        Grid.SetColumn(u, 2);
        g.Children.Add(u);
        return g;
    }

    /// <summary>Aplica uma alteração à operação <paramref name="i"/> e refaz tela e programa. <paramref name="rebuild"/>: refazer também o painel (muda quais campos aparecem).</summary>
    private void Edit(int i, Func<CamOperationSpec, CamOperationSpec> change, bool rebuild = false)
    {
        if (i >= _job.Operations.Count)
        {
            return;
        }

        Snap();
        CamTool before = _job.Operations[i].Tool;
        _job.Operations[i] = change(_job.Operations[i]);
        CamTool after = _job.Operations[i].Tool;
        if (after.Number == before.Number && (after.Diameter != before.Diameter || after.Name != before.Name))
        {
            // Geometria da ferramenta vale para todo o mesmo T; rotação e avanços ficam em cada operação
            _job.ApplyToolGeometry(after.Number, after.Diameter, after.Name);
        }

        Refresh(op: i, props: rebuild);
    }

    private void Num(int i, string label, string unit, double value, Func<CamOperationSpec, double, CamOperationSpec> apply)
    {
        var box = new TextBox { Text = Fmt(value), MinHeight = 26 };
        void Commit()
        {
            double v = ParseNum(box.Text);
            if (double.IsNaN(v))
            {
                box.Foreground = Avalonia.Media.Brushes.IndianRed;
                Errors.Text = $"• Valor inválido em \"{label}\".";
                return;
            }

            box.ClearValue(TemplatedControl.ForegroundProperty);
            if (Math.Abs(v - value) > 1e-9)
            {
                value = v;
                Edit(i, x => apply(x, v));
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
            }
        };
        PropsPanel.Children.Add(Row(label, box, unit));
    }

    private void Text(int i, string label, string value, string hint, Func<CamOperationSpec, string, CamOperationSpec> apply)
    {
        var box = new TextBox { Text = value, Watermark = hint, MinHeight = 26 };
        void Commit()
        {
            string v = box.Text ?? "";
            if (v != value)
            {
                value = v;
                Edit(i, x => apply(x, v));
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
            }
        };
        PropsPanel.Children.Add(Row(label, box));
    }

    private void Pick(int i, string label, string[] options, int selected, Func<CamOperationSpec, int, CamOperationSpec> apply, bool rebuild = false)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string o in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = o });
        }

        combo.SelectedIndex = Math.Clamp(selected, 0, options.Length - 1);
        int current = combo.SelectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex != current)
            {
                current = combo.SelectedIndex;
                Edit(i, x => apply(x, current), rebuild);
            }
        };
        PropsPanel.Children.Add(Row(label, combo));
    }

    private void Check(int i, string label, bool value, Func<CamOperationSpec, bool, CamOperationSpec> apply, bool rebuild = false)
    {
        var box = new CheckBox { Content = label, IsChecked = value, FontSize = 12 };
        box.IsCheckedChanged += (_, _) =>
        {
            bool now = box.IsChecked == true;
            if (now != value)
            {
                value = now;
                Edit(i, x => apply(x, now), rebuild);
            }
        };
        PropsPanel.Children.Add(box);
    }

    // ---------------------------------------------------------------- banco de ferramentas

    private static bool IsMillTool(ToolEntry e) =>
        !(Enum.TryParse(e.Kind, out ToolKind kind) && new SetupTool { Kind = kind }.IsLathe);

    private void BankPicker(int i)
    {
        List<ToolEntry> bank = (ToolsSource?.Invoke() ?? []).Where(IsMillTool).OrderBy(t => t.Number).ToList();
        if (bank.Count == 0)
        {
            return;
        }

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Ferramenta do banco…" };
        foreach (ToolEntry t in bank)
        {
            combo.Items.Add(new ComboBoxItem { Content = $"T{t.Number} · {(t.Description.Length > 0 ? t.Description : "Fresa")} Ø{Fmt(t.Diameter)}" });
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0)
            {
                return;
            }

            ToolEntry t = bank[combo.SelectedIndex];
            Edit(i, x => x with
            {
                Tool = new CamTool(t.Number, t.Diameter, t.SpindleRpm > 0 ? t.SpindleRpm : x.Tool.SpindleRpm, t.FeedXY > 0 ? t.FeedXY : x.Tool.FeedXY, t.FeedZ > 0 ? t.FeedZ : x.Tool.FeedZ, t.Description),
            }, rebuild: true);
        };
        PropsPanel.Children.Add(combo);
    }
}
