using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>Painel de propriedades da operação de torno selecionada: cada campo vira uma alteração do <see cref="LatheOperationSpec"/>.</summary>
public partial class LatheJobView
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
        PropsPanel.Children.Add(new TextBlock { Text = "Propriedades", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        if (SelectedOp() is not { } i || i >= _job.Operations.Count)
        {
            PropsPanel.Children.Add(new TextBlock { Text = "Selecione uma operação na árvore para editar os dados dela.", Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
            return;
        }

        LatheOperationSpec s = _job.Operations[i];
        PropsPanel.Children.Add(new TextBlock { Text = LatheJobInfo.OpTitle(i, s), Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
        Text(i, "Nome", s.Label, "padrão", (x, v) => x with { Label = v.Trim() });

        Section("Ferramenta");
        Num(i, "Ferramenta (T)", "", s.Tool.Number, (x, v) => x with { Tool = x.Tool with { Number = (int)Math.Round(v) } });
        Text(i, "Nome da ferramenta", s.Tool.Name, "ex.: Desbaste CNMG", (x, v) => x with { Tool = x.Tool with { Name = v.Trim() } });
        Num(i, "Velocidade de corte (G96)", "m/min", s.Tool.SurfaceSpeed, (x, v) => x with { Tool = x.Tool with { SurfaceSpeed = v } });
        Num(i, "Rotação máxima", "rpm", s.Tool.MaxRpm, (x, v) => x with { Tool = x.Tool with { MaxRpm = v } });
        Num(i, "Rotação fixa (0 = G96)", "rpm", s.Tool.Rpm, (x, v) => x with { Tool = x.Tool with { Rpm = v } });

        Section("Segurança");
        Num(i, "Z seguro", "mm", s.SafeZ, (x, v) => x with { SafeZ = v });
        Num(i, "Ø seguro (troca)", "mm", s.SafeDiameter, (x, v) => x with { SafeDiameter = v });
        Num(i, "Folga de aproximação", "mm", s.Clearance, (x, v) => x with { Clearance = v });

        switch (s.Kind)
        {
            case LatheOpKind.Turning when s.Turning is { } t:
                Section(s.IsInternal ? "Torneamento interno" : "Torneamento externo");
                Num(i, s.IsInternal ? "Ø do pré-furo" : "Ø do bruto", "mm", t.StockDiameter, (x, v) => x with { Turning = t with { StockDiameter = v } });
                Check(i, "Desbaste", t.Rough, (x, b) => x with { Turning = t with { Rough = b } });
                Check(i, "Acabamento", t.Finish, (x, b) => x with { Turning = t with { Finish = b } });
                Num(i, "Profundidade de corte (raio)", "mm", t.DepthOfCut, (x, v) => x with { Turning = t with { DepthOfCut = v } });
                Num(i, "Recuo", "mm", t.Retract, (x, v) => x with { Turning = t with { Retract = v } });
                Num(i, "Sobremetal radial", "mm", t.AllowanceR, (x, v) => x with { Turning = t with { AllowanceR = v } });
                Num(i, "Sobremetal em Z", "mm", t.AllowanceZ, (x, v) => x with { Turning = t with { AllowanceZ = v } });
                Num(i, "Avanço do desbaste", "mm/rot", t.RoughFeed, (x, v) => x with { Turning = t with { RoughFeed = v } });
                Num(i, "Avanço do acabamento", "mm/rot", t.FinishFeed, (x, v) => x with { Turning = t with { FinishFeed = v } });
                break;
            case LatheOpKind.Facing when s.Facing is { } f:
                Section("Faceamento");
                Num(i, "Ø do bruto", "mm", f.StockDiameter, (x, v) => x with { Facing = f with { StockDiameter = v } });
                Num(i, "Ø interno (0 = até o centro)", "mm", f.InnerDiameter, (x, v) => x with { Facing = f with { InnerDiameter = v } });
                Num(i, "Z da face do bruto", "mm", f.StockFaceZ, (x, v) => x with { Facing = f with { StockFaceZ = v } });
                Num(i, "Z da face pronta", "mm", f.FinalFaceZ, (x, v) => x with { Facing = f with { FinalFaceZ = v } });
                Num(i, "Passe em Z", "mm", f.DepthOfCut, (x, v) => x with { Facing = f with { DepthOfCut = v } });
                Num(i, "Avanço", "mm/rot", f.Feed, (x, v) => x with { Facing = f with { Feed = v } });
                break;
            case LatheOpKind.Drilling when s.Drill is { } d:
                Section("Furação axial");
                Num(i, "Z da face", "mm", d.TopZ, (x, v) => x with { Drill = d with { TopZ = v } });
                Num(i, "Z do fundo", "mm", d.BottomZ, (x, v) => x with { Drill = d with { BottomZ = v } });
                Num(i, "Bicada", "mm", d.Peck, (x, v) => x with { Drill = d with { Peck = v } });
                Num(i, "Avanço", "mm/rot", d.Feed, (x, v) => x with { Drill = d with { Feed = v } });
                break;
            case LatheOpKind.Threading when s.Thread is { } th:
                Section(s.IsInternal ? "Rosca interna" : "Rosca externa");
                Num(i, s.IsInternal ? "Ø nominal (raiz)" : "Ø nominal (crista)", "mm", th.MajorDiameter, (x, v) => x with { Thread = th with { MajorDiameter = v } });
                Num(i, "Passo", "mm", th.Pitch, (x, v) => x with { Thread = th with { Pitch = v } });
                Num(i, "Z do início", "mm", th.StartZ, (x, v) => x with { Thread = th with { StartZ = v } });
                Num(i, "Z do fim", "mm", th.EndZ, (x, v) => x with { Thread = th with { EndZ = v } });
                Num(i, "Rotação (fixa)", "rpm", th.Rpm, (x, v) => x with { Thread = th with { Rpm = v } });
                Num(i, "Altura do filete (0 = ISO)", "mm", th.Height, (x, v) => x with { Thread = th with { Height = v } });
                Num(i, "Passes de acabamento", "", th.FinishPasses, (x, v) => x with { Thread = th with { FinishPasses = (int)Math.Round(v) } });
                Num(i, "Sobremetal de acabamento", "mm", th.Allowance, (x, v) => x with { Thread = th with { Allowance = v } });
                break;
            case LatheOpKind.Grooving when s.Groove is { } g:
                Section(s.IsInternal ? "Canal interno" : "Canal externo");
                Num(i, s.IsInternal ? "Ø do furo" : "Ø da superfície", "mm", g.SurfaceDiameter, (x, v) => x with { Groove = g with { SurfaceDiameter = v } });
                Num(i, "Ø do fundo do canal", "mm", g.BottomDiameter, (x, v) => x with { Groove = g with { BottomDiameter = v } });
                Num(i, "Z da parede de cima", "mm", g.ZStart, (x, v) => x with { Groove = g with { ZStart = v } });
                Num(i, "Largura do canal", "mm", g.Width, (x, v) => x with { Groove = g with { Width = v } });
                Num(i, "Largura da ferramenta", "mm", g.ToolWidth, (x, v) => x with { Groove = g with { ToolWidth = v } });
                Num(i, "Mergulho por passe", "mm", g.Peck, (x, v) => x with { Groove = g with { Peck = v } });
                Num(i, "Sobremetal de acabamento (0 = sem)", "mm", g.Finish ? g.Allowance : 0, (x, v) => x with { Groove = g with { Allowance = v, Finish = v > 0 } });
                Num(i, "Avanço", "mm/rot", g.Feed, (x, v) => x with { Groove = g with { Feed = v } });
                break;
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
    private void Edit(int i, Func<LatheOperationSpec, LatheOperationSpec> change, bool rebuild = false)
    {
        if (i >= _job.Operations.Count)
        {
            return;
        }

        _job.Operations[i] = change(_job.Operations[i]);
        Refresh(op: i, props: rebuild);
    }

    private void Num(int i, string label, string unit, double value, Func<LatheOperationSpec, double, LatheOperationSpec> apply)
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

    private void Text(int i, string label, string value, string hint, Func<LatheOperationSpec, string, LatheOperationSpec> apply)
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

    private void Pick(int i, string label, string[] options, int selected, Func<LatheOperationSpec, int, LatheOperationSpec> apply, bool rebuild = false)
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

    private void Check(int i, string label, bool value, Func<LatheOperationSpec, bool, LatheOperationSpec> apply, bool rebuild = false)
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
}
