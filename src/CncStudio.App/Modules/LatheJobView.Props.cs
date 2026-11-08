using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using CncStudio.App.Settings;
using CncStudio.Core.Cam;
using CncStudio.Core.Setup;

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

    /// <summary>Onde os editores escrevem: o painel direito, ou um cartão da Preparação enquanto ele é montado.</summary>
    private StackPanel? _propsTarget;

    private StackPanel PropsPanel => _propsTarget ?? OpPanel;

    /// <summary>Monta os cartões da Preparação num alvo (o mesmo editor do painel), e volta ao painel no fim.</summary>
    private void Into(StackPanel target, Action build)
    {
        target.Children.Clear();
        _propsTarget = target;
        try
        {
            build();
        }
        finally
        {
            _propsTarget = null;
        }
    }

    /// <summary>
    /// Cartões da Preparação: Peça (o editor de perfil de antes, no XAML), Bruto (só leitura: o torno não tem bruto próprio),
    /// Fixação do torno (o editor de antes) e Zero (só texto, como na fresa). Sem campo novo.
    /// </summary>
    private void BuildPrep()
    {
        PrepProfileTitle.Text = LatheJobInfo.ProfileTitle(_job.Profile);
        Into(PrepStock, StockProps);
        Into(PrepFixation, () =>
        {
            PropsPanel.Children.Add(new TextBlock { Text = "Fixação do torno", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
            BuildFixationProps();
        });
        Into(PrepOrigin, ZeroProps);
    }

    /// <summary>Bruto (barra): só leitura dos Ø do bruto digitados nas operações; cada linha leva à operação.</summary>
    private void StockProps()
    {
        PropsPanel.Children.Add(new TextBlock { Text = "Bruto (barra)", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        PropsPanel.Children.Add(new TextBlock
        {
            Text = "O torno não tem bruto próprio: o Ø do bruto é o de cada operação de torneamento externo e faceamento. Para mudar, edite a operação.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12, Foreground = Avalonia.Media.Brush.Parse("#9aa7b5"),
        });
        var groups = LatheJobInfo.StockDiameters(_job);
        if (groups.Count == 0)
        {
            PropsPanel.Children.Add(new TextBlock { Text = "Nenhuma operação com Ø do bruto ainda.", FontSize = 12 });
            return;
        }

        foreach ((string text, IReadOnlyList<int> ops) in groups)
        {
            foreach (int op in ops)
            {
                int index = op;
                var go = new Button
                {
                    Content = $"Ø {text} mm · {LatheJobInfo.OpTitle(op, _job.Operations[op])}", FontSize = 12, Padding = new Avalonia.Thickness(8, 3),
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                ToolTip.SetTip(go, "Abrir a operação na aba Operações");
                go.Click += (_, _) => GoToOp(index);
                PropsPanel.Children.Add(go);
            }
        }
    }

    /// <summary>Zero da fixação 1: só o que o trabalho já tem (G5x, X0 no centro, Z0 do perfil). Sem campo novo.</summary>
    private void ZeroProps()
    {
        PropsPanel.Children.Add(new TextBlock { Text = "Zero da fixação 1", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        PropsPanel.Children.Add(new TextBlock { Text = LatheJobInfo.OriginTitle(_job), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
        PropsPanel.Children.Add(UiKit.StatusNote("Origem " + CamJobInfo.OriginState().Text + ": confira na máquina. Escolher o zero pela peça e ter um zero por fixação vêm em breve.", "pending"));
    }

    private void BuildProps()
    {
        PropsPanel.Children.Clear();
        PropsPanel.Children.Add(new TextBlock { Text = "Propriedades", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        if (SelectedOp() is not { } i || i >= _job.Operations.Count)
        {
            string hint = (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: not "op" }
                ? "Peça, bruto, fixação e zero ficam na aba Preparação."
                : "Selecione uma operação na árvore para editar os dados, ou adicione uma pela faixa.";
            PropsPanel.Children.Add(new TextBlock { Text = hint, Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
            return;
        }

        LatheOperationSpec s = _job.Operations[i];
        PropsPanel.Children.Add(new TextBlock { Text = LatheJobInfo.OpTitle(i, s), Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
        Text(i, "Nome", s.Label, "padrão", (x, v) => x with { Label = v.Trim() });

        Section("Ferramenta");
        BankPicker(i);
        Num(i, "Ferramenta (T)", "", s.Tool.Number, (x, v) => x with { Tool = x.Tool with { Number = (int)Math.Round(v) } });
        Text(i, "Nome da ferramenta", s.Tool.Name, "ex.: Desbaste CNMG", (x, v) => x with { Tool = x.Tool with { Name = v.Trim() } });
        Section("Condição de corte");
        Num(i, "Velocidade de corte (G96)", "m/min", s.Tool.SurfaceSpeed, (x, v) => x with { Tool = x.Tool with { SurfaceSpeed = v } });
        Num(i, "Rotação máxima", "rpm", s.Tool.MaxRpm, (x, v) => x with { Tool = x.Tool with { MaxRpm = v } });
        Num(i, "Rotação fixa (0 = G96)", "rpm", s.Tool.Rpm, (x, v) => x with { Tool = x.Tool with { Rpm = v } });
        if (LatheJobInfo.SpindleWarning(s.Tool, MachineSource?.Invoke()?.MaxSpindleRpm) is { } spindle)
        {
            // só conferência: a rotação e o programa continuam os digitados
            PropsPanel.Children.Add(UiKit.StatusNote(spindle, "pending"));
        }

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

    /// <summary>Fixação: tipo e medidas opcionais. Só informativa; o aviso "não verificada" fica sempre visível.</summary>
    private void BuildFixationProps()
    {
        PropsPanel.Children.Add(UiKit.StatusNote("Fixação não verificada: o app não confere se a peça está presa nem colisão com a placa. Primeira execução sempre no ar.", "pending"));
        LatheFixation? f = _job.Fixation;
        string[] kinds = ["Não informada", "Castanhas", "Placa", "Entre pontas (castanhas + contraponto)"];
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string k in kinds)
        {
            combo.Items.Add(new ComboBoxItem { Content = k });
        }

        combo.SelectedIndex = f is null ? 0 : (int)f.Kind + 1;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0)
            {
                return;
            }

            // trocar o tipo guarda as medidas já digitadas; "Não informada" apaga a fixação
            LatheFixation? now = combo.SelectedIndex == 0 ? null : (_job.Fixation ?? new LatheFixation(LatheFixationKind.Jaws)) with { Kind = (LatheFixationKind)(combo.SelectedIndex - 1) };
            SetFixation(now, rebuild: true);
        };
        PropsPanel.Children.Add(Row("Tipo", combo));
        if (f is null)
        {
            return;
        }

        FixNum("Ø de fixação", f.GripDiameter, (x, v) => x with { GripDiameter = v });
        FixNum("Comprimento preso", f.GripLength, (x, v) => x with { GripLength = v });
        FixNum("Balanço livre", f.StickOut, (x, v) => x with { StickOut = v });
        if (f.Validate() is { } error)
        {
            PropsPanel.Children.Add(UiKit.StatusNote(error, "bad"));
        }
    }

    /// <summary>Medida opcional da fixação: vazio = não informada.</summary>
    private void FixNum(string label, double? value, Func<LatheFixation, double?, LatheFixation> apply)
    {
        var box = new TextBox { Text = value is { } v0 ? Fmt(v0) : "", Watermark = "não informado", MinHeight = 26 };
        void Commit()
        {
            double? v = null;
            if (!string.IsNullOrWhiteSpace(box.Text))
            {
                double parsed = ParseNum(box.Text);
                if (double.IsNaN(parsed))
                {
                    box.Foreground = Avalonia.Media.Brushes.IndianRed;
                    Errors.Text = $"• Valor inválido em \"{label}\".";
                    return;
                }

                v = parsed;
            }

            box.ClearValue(TemplatedControl.ForegroundProperty);
            if (v != value && _job.Fixation is { } f)
            {
                value = v;
                LatheFixation next = apply(f, v);
                // sem refazer o painel (o foco segue para o próximo campo); medida inválida fica em vermelho aqui e na árvore
                if (v is { } x && !(x > 0))
                {
                    box.Foreground = Avalonia.Media.Brushes.IndianRed;
                }

                SetFixation(next, rebuild: false);
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
        PropsPanel.Children.Add(Row(label, box, "mm"));
    }

    private void SetFixation(LatheFixation? f, bool rebuild)
    {
        _job.Fixation = f;
        Refresh(keep: true, props: rebuild);
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

        Snap();
        LatheTool before = _job.Operations[i].Tool;
        _job.Operations[i] = change(_job.Operations[i]);
        LatheTool after = _job.Operations[i].Tool;
        if (after.Number == before.Number && after.Name != before.Name)
        {
            _job.ApplyToolGeometry(after.Number, after.Name);
        }

        Refresh(op: i, props: rebuild);
    }

    /// <summary>
    /// Campo numérico da operação (regra do CAM-12, UX-04e). Vazio ou com texto marca o campo na operação
    /// (<see cref="LatheJobInfo.SetMissing"/>): ela fica Incompleta, não gera e o número antigo não é usado nem mostrado. O campo
    /// obrigatório vazio fica com borda tracejada laranja. Um valor válido grava e tira a marca.
    /// </summary>
    private void Num(int i, string label, string unit, double value, Func<LatheOperationSpec, double, LatheOperationSpec> apply)
    {
        bool missing = i < _job.Operations.Count && _job.Operations[i].MissingFields.Contains(label);
        var box = new TextBox { Text = missing ? "" : Fmt(value), MinHeight = 26, Watermark = "sem valor" };
        (Grid field, Action<string?> Mark) = CamShell.DashedField(box);

        if (missing)
        {
            Mark($"Falta {label}: digite um valor. O valor antigo não é usado.");
        }

        void Commit()
        {
            string typed = (box.Text ?? "").Trim();
            double v = ParseNum(typed);
            if (typed.Length == 0 || double.IsNaN(v))
            {
                string why = typed.Length == 0 ? $"Falta {label}: digite um valor." : $"\"{typed}\" não é um número. Falta {label}.";
                Mark(why);
                Errors.Text = "• " + why;
                if (!missing)
                {
                    missing = true;
                    Edit(i, x => LatheJobInfo.SetMissing(x, label, true));
                }

                return;
            }

            Mark(null);
            if (missing || Math.Abs(v - value) > 1e-9)
            {
                missing = false;
                value = v;
                Edit(i, x => LatheJobInfo.SetMissing(apply(x, v), label, false));
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
        PropsPanel.Children.Add(Row(label, field, unit));
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

    // ---------------------------------------------------------------- banco de ferramentas

    private static bool IsLatheTool(ToolEntry e) =>
        Enum.TryParse(e.Kind, out ToolKind kind) && new SetupTool { Kind = kind }.IsLathe;

    private void BankPicker(int i)
    {
        List<ToolEntry> bank = (ToolsSource?.Invoke() ?? []).Where(IsLatheTool).OrderBy(t => t.Number).ToList();
        if (bank.Count == 0)
        {
            return;
        }

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Ferramenta do banco…" };
        foreach (ToolEntry t in bank)
        {
            combo.Items.Add(new ComboBoxItem { Content = $"T{t.Number} · {(t.Description.Length > 0 ? t.Description : "Torno")}" });
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0)
            {
                return;
            }

            ToolEntry t = bank[combo.SelectedIndex];
            Edit(i, x => x with { Tool = x.Tool with { Number = t.Number, Name = t.Description, Rpm = t.SpindleRpm > 0 ? t.SpindleRpm : x.Tool.Rpm } }, rebuild: true);
        };
        PropsPanel.Children.Add(combo);
    }
}
