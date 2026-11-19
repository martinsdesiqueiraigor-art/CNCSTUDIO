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
        MachineCard(PrepMachine);
        PrepProfileTitle.Text = LatheJobInfo.ProfileTitle(_job.Profile);
        Into(PrepStock, StockProps);
        Into(PrepFixation, () =>
        {
            PropsPanel.Children.Add(new TextBlock { Text = "Fixação do torno", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
            BuildFixationProps();
            // TORNO-T1: Programa nº, G5x e refrigeração digitados, no cartão da fixação (como na fresa)
            ProgramFields();
        });
        Into(PrepOrigin, ZeroProps);
    }

    /// <summary>Zero do trabalho (TORNO-T5): X0 no centro e o Z0 escolhido (desenho do perfil, face pronta ou face oposta).</summary>
    private void ZeroProps()
    {
        PropsPanel.Children.Add(new TextBlock { Text = "Zero do trabalho", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        PropsPanel.Children.Add(new TextBlock { Text = LatheJobInfo.OriginTitle(_job), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string k in LatheZero.ChoiceNames)
        {
            combo.Items.Add(new ComboBoxItem { Content = k });
        }

        combo.SelectedIndex = _job.ZeroFace is { } zf ? (int)zf + 1 : 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0)
            {
                return;
            }

            _job.ZeroFace = combo.SelectedIndex == 0 ? null : (LatheZeroFace)(combo.SelectedIndex - 1);
            Refresh(keep: true, props: true);
        };
        PropsPanel.Children.Add(Row("Z0", combo));
        PropsPanel.Children.Add(new TextBlock
        {
            Text = "Os Z digitados nas operações continuam no desenho do perfil; só o programa sai deslocado para o Z0 escolhido.",
            Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12,
        });
        if (LatheZero.Problems(_job).Count > 0)
        {
            PropsPanel.Children.Add(UiKit.StatusNote(LatheZero.NeedsProfile + ".", "bad"));
        }

        PropsPanel.Children.Add(UiKit.StatusNote("Origem " + CamJobInfo.OriginState().Text + ": confira na máquina.", "pending"));
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
        if (ToolBankPick.LatheConflict(s, ActiveBank) is { } conflict)
        {
            PropsPanel.Children.Add(UiKit.StatusNote(conflict, "pending"));
        }

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
                Num(i, s.IsInternal ? "Ø do pré-furo" : "Ø do bruto", "mm", t.StockDiameter, (x, v) => x with { Turning = x.Turning! with { StockDiameter = v } });
                Check(i, "Desbaste", t.Rough, (x, b) => x with { Turning = x.Turning! with { Rough = b } });
                Check(i, "Acabamento", t.Finish, (x, b) => x with { Turning = x.Turning! with { Finish = b } });
                Num(i, "Profundidade de corte (raio)", "mm", t.DepthOfCut, (x, v) => x with { Turning = x.Turning! with { DepthOfCut = v } });
                Num(i, "Recuo", "mm", t.Retract, (x, v) => x with { Turning = x.Turning! with { Retract = v } });
                Num(i, "Sobremetal radial", "mm", t.AllowanceR, (x, v) => x with { Turning = x.Turning! with { AllowanceR = v } });
                Num(i, "Sobremetal em Z", "mm", t.AllowanceZ, (x, v) => x with { Turning = x.Turning! with { AllowanceZ = v } });
                Num(i, "Avanço do desbaste", "mm/rot", t.RoughFeed, (x, v) => x with { Turning = x.Turning! with { RoughFeed = v } });
                Num(i, "Avanço do acabamento", "mm/rot", t.FinishFeed, (x, v) => x with { Turning = x.Turning! with { FinishFeed = v } });
                break;
            case LatheOpKind.Facing when s.Facing is { } f:
                Section("Faceamento");
                Num(i, "Ø do bruto", "mm", f.StockDiameter, (x, v) => x with { Facing = x.Facing! with { StockDiameter = v } });
                Num(i, "Ø interno (0 = até o centro)", "mm", f.InnerDiameter, (x, v) => x with { Facing = x.Facing! with { InnerDiameter = v } });
                Num(i, "Z da face do bruto", "mm", f.StockFaceZ, (x, v) => x with { Facing = x.Facing! with { StockFaceZ = v } });
                Num(i, "Z da face pronta", "mm", f.FinalFaceZ, (x, v) => x with { Facing = x.Facing! with { FinalFaceZ = v } });
                Num(i, "Passe em Z", "mm", f.DepthOfCut, (x, v) => x with { Facing = x.Facing! with { DepthOfCut = v } });
                Num(i, "Avanço", "mm/rot", f.Feed, (x, v) => x with { Facing = x.Facing! with { Feed = v } });
                break;
            case LatheOpKind.Drilling when s.Drill is { } d:
                Section("Furação axial");
                Num(i, "Z da face", "mm", d.TopZ, (x, v) => x with { Drill = x.Drill! with { TopZ = v } });
                Num(i, "Z do fundo", "mm", d.BottomZ, (x, v) => x with { Drill = x.Drill! with { BottomZ = v } });
                Num(i, "Bicada", "mm", d.Peck, (x, v) => x with { Drill = x.Drill! with { Peck = v } });
                Num(i, "Avanço", "mm/rot", d.Feed, (x, v) => x with { Drill = x.Drill! with { Feed = v } });
                break;
            case LatheOpKind.Threading when s.Thread is { } th:
                Section(s.IsInternal ? "Rosca interna" : "Rosca externa");
                Num(i, s.IsInternal ? "Ø nominal (raiz)" : "Ø nominal (crista)", "mm", th.MajorDiameter, (x, v) => x with { Thread = x.Thread! with { MajorDiameter = v } });
                Num(i, "Passo", "mm", th.Pitch, (x, v) => x with { Thread = x.Thread! with { Pitch = v } });
                Num(i, "Z do início", "mm", th.StartZ, (x, v) => x with { Thread = x.Thread! with { StartZ = v } });
                Num(i, "Z do fim", "mm", th.EndZ, (x, v) => x with { Thread = x.Thread! with { EndZ = v } });
                Num(i, "Rotação (fixa)", "rpm", th.Rpm, (x, v) => x with { Thread = x.Thread! with { Rpm = v } });
                Num(i, "Altura do filete (0 = ISO)", "mm", th.Height, (x, v) => x with { Thread = x.Thread! with { Height = v } });
                Num(i, "Passes de acabamento", "", th.FinishPasses, (x, v) => x with { Thread = x.Thread! with { FinishPasses = (int)Math.Round(v) } });
                Num(i, "Sobremetal de acabamento", "mm", th.Allowance, (x, v) => x with { Thread = x.Thread! with { Allowance = v } });
                break;
            case LatheOpKind.Grooving when s.Groove is { } g:
                Section(s.IsInternal ? "Canal interno" : "Canal externo");
                Num(i, s.IsInternal ? "Ø do furo" : "Ø da superfície", "mm", g.SurfaceDiameter, (x, v) => x with { Groove = x.Groove! with { SurfaceDiameter = v } });
                Num(i, "Ø do fundo do canal", "mm", g.BottomDiameter, (x, v) => x with { Groove = x.Groove! with { BottomDiameter = v } });
                Num(i, "Z da parede de cima", "mm", g.ZStart, (x, v) => x with { Groove = x.Groove! with { ZStart = v } });
                Num(i, "Largura do canal", "mm", g.Width, (x, v) => x with { Groove = x.Groove! with { Width = v } });
                Num(i, "Largura da ferramenta", "mm", g.ToolWidth, (x, v) => x with { Groove = x.Groove! with { ToolWidth = v } });
                Num(i, "Mergulho por passe", "mm", g.Peck, (x, v) => x with { Groove = x.Groove! with { Peck = v } });
                Num(i, "Sobremetal de acabamento (0 = sem)", "mm", g.Finish ? g.Allowance : 0, (x, v) => x with { Groove = x.Groove! with { Allowance = v, Finish = v > 0 } });
                Num(i, "Avanço", "mm/rot", g.Feed, (x, v) => x with { Groove = x.Groove! with { Feed = v } });
                break;
            case LatheOpKind.FaceGrooving when s.FaceGroove is { } fg:
                Section("Canal de face");
                PropsPanel.Children.Add(UiKit.StatusNote(LatheFaceGrooving.Warning, "pending"));
                Num(i, LatheJobInfo.FaceOuterField, "mm", fg.OuterDiameter, (x, v) => x with { FaceGroove = x.FaceGroove! with { OuterDiameter = v } });
                Num(i, LatheJobInfo.FaceInnerField, "mm", fg.InnerDiameter, (x, v) => x with { FaceGroove = x.FaceGroove! with { InnerDiameter = v } });
                Num(i, "Z da face", "mm", fg.FaceZ, (x, v) => x with { FaceGroove = x.FaceGroove! with { FaceZ = v } });
                Num(i, "Z do fundo", "mm", fg.BottomZ, (x, v) => x with { FaceGroove = x.FaceGroove! with { BottomZ = v } });
                Num(i, LatheJobInfo.ToolWidthField, "mm", fg.ToolWidth, (x, v) => x with { FaceGroove = x.FaceGroove! with { ToolWidth = v } });
                Num(i, "Mergulho por passe", "mm", fg.Peck, (x, v) => x with { FaceGroove = x.FaceGroove! with { Peck = v } });
                Num(i, "Recuo (quebra-cavaco)", "mm", fg.Retract, (x, v) => x with { FaceGroove = x.FaceGroove! with { Retract = v } });
                Num(i, "Avanço", "mm/rot", fg.Feed, (x, v) => x with { FaceGroove = x.FaceGroove! with { Feed = v } });
                Num(i, LatheJobInfo.ReducedFeedField, "mm/rot", fg.ReducedFeed, (x, v) => x with { FaceGroove = x.FaceGroove! with { ReducedFeed = v } });
                Num(i, LatheJobInfo.ReducedFromField, "mm", fg.ReducedFromDiameter, (x, v) => x with { FaceGroove = x.FaceGroove! with { ReducedFromDiameter = v } });
                Num(i, "Sobremetal de acabamento (0 = sem)", "mm", fg.Allowance, (x, v) => x with { FaceGroove = x.FaceGroove! with { Allowance = v } });
                Section("Bedame de face (faixa de Ø)");
                Num(i, LatheJobInfo.FaceToolMinField, "mm", fg.ToolMinDiameter, (x, v) => x with { FaceGroove = x.FaceGroove! with { ToolMinDiameter = v } });
                Num(i, LatheJobInfo.FaceToolMaxField, "mm", fg.ToolMaxDiameter, (x, v) => x with { FaceGroove = x.FaceGroove! with { ToolMaxDiameter = v } });
                break;
            case LatheOpKind.Parting when s.Parting is { } pt:
                Section("Sangramento");
                PropsPanel.Children.Add(UiKit.StatusNote(LatheParting.Warning, "pending"));
                if (pt.FinalDiameter == 0 && !s.MissingFields.Contains(LatheJobInfo.PartingFinalField))
                {
                    // aviso fixo de peça solta: o app não verifica fixação nem a queda
                    PropsPanel.Children.Add(UiKit.StatusNote(LatheParting.LoosePartWarning, "bad"));
                }

                PropsPanel.Children.Add(UiKit.StatusNote(LatheParting.LastOperation, "pending"));
                Num(i, "Z do corte", "mm", pt.CutZ, (x, v) => x with { Parting = x.Parting! with { CutZ = v } });
                Num(i, "Ø de início", "mm", pt.StartDiameter, (x, v) => x with { Parting = x.Parting! with { StartDiameter = v } });
                Num(i, LatheJobInfo.PartingFinalField, "mm", pt.FinalDiameter, (x, v) => x with { Parting = x.Parting! with { FinalDiameter = v } }, rebuild: true);
                Num(i, LatheJobInfo.ToolWidthField, "mm", pt.ToolWidth, (x, v) => x with { Parting = x.Parting! with { ToolWidth = v } });
                Num(i, "Avanço", "mm/rot", pt.Feed, (x, v) => x with { Parting = x.Parting! with { Feed = v } });
                Num(i, LatheJobInfo.ReducedFeedField, "mm/rot", pt.ReducedFeed, (x, v) => x with { Parting = x.Parting! with { ReducedFeed = v } });
                Num(i, LatheJobInfo.ReducedFromField, "mm", pt.ReducedFromDiameter, (x, v) => x with { Parting = x.Parting! with { ReducedFromDiameter = v } });
                Num(i, "Mergulho por passe", "mm", pt.Peck, (x, v) => x with { Parting = x.Parting! with { Peck = v } });
                Num(i, "Recuo (quebra-cavaco)", "mm", pt.Retract, (x, v) => x with { Parting = x.Parting! with { Retract = v } });
                break;
        }
    }

    /// <summary>Fixação: tipo e medidas opcionais. Só informativa; o aviso "não verificada" fica sempre visível.</summary>
    private void BuildFixationProps()
    {
        PropsPanel.Children.Add(UiKit.StatusNote("Fixação não verificada: o app não confere se a peça está presa nem colisão com a placa. Primeira execução sempre no ar.", "pending"));
        LatheFixation? f = _job.Fixation;
        string[] kinds = ["Não informada", .. LatheFixation.KindNames];
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
            // DESK-01: "Entre pontas (castanhas + contraponto)" já marca o contraponto (dá para desmarcar)
            if (now is { Kind: LatheFixationKind.Center } && _job.Fixation?.Kind != LatheFixationKind.Center)
            {
                now = now with { Tailstock = true };
            }
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
        // TORNO-T4: apoios informativos
        FixCheck("Contraponto", f.Tailstock, (x, v) => x with { Tailstock = v });
        FixCheck("Luneta", f.SteadyRest, (x, v) => x with { SteadyRest = v });
        if (f.Validate() is { } error)
        {
            PropsPanel.Children.Add(UiKit.StatusNote(error, "bad"));
        }
    }

    /// <summary>Apoio da fixação (contraponto ou luneta): marcado ou não. Só informativo.</summary>
    private void FixCheck(string label, bool value, Func<LatheFixation, bool, LatheFixation> apply)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.IsCheckedChanged += (_, _) =>
        {
            if (_job.Fixation is { } f)
            {
                SetFixation(apply(f, box.IsChecked == true), rebuild: false);
            }
        };
        PropsPanel.Children.Add(box);
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
    private void Num(int i, string label, string unit, double value, Func<LatheOperationSpec, double, LatheOperationSpec> apply, bool rebuild = false)
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
                    // TORNO-T1: o número antigo é zerado (o arquivo pode ser salvo com o campo vazio e não guarda valor que não aparece)
                    Edit(i, x => LatheJobInfo.SetMissing(apply(x, 0), label, true), rebuild);
                }

                return;
            }

            Mark(null);
            if (missing || Math.Abs(v - value) > 1e-9)
            {
                missing = false;
                value = v;
                Edit(i, x => LatheJobInfo.SetMissing(apply(x, v), label, false), rebuild);
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

    private ComboBox? _bankSelector;
    private bool _fillingBanks;

    /// <summary>Seletor do banco ativo do torno no fim da faixa (UX-04h D1, como na fresa). A escolha fica no App, nunca no trabalho.</summary>
    private void BuildBankSelector()
    {
        _bankSelector = new ComboBox { Width = 150, PlaceholderText = "Nenhum banco de torno", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_bankSelector, "Banco de onde a operação escolhe a ferramenta e de onde o Code lê as ferramentas do torno. Fica guardado no Casillas, não no arquivo do trabalho.");
        Avalonia.Automation.AutomationProperties.SetName(_bankSelector, "Banco ativo");
        _bankSelector.SelectionChanged += (_, _) =>
        {
            if (!_fillingBanks && _bankSelector.SelectedItem is ComboBoxItem { Tag: string id } && id != ActiveBank?.Id)
            {
                SetActiveBank?.Invoke(id);
                // não refazer a lista dentro do próprio evento de seleção (o Avalonia não aceita): só o painel
                if (SelectedOp() is not null)
                {
                    BuildProps();
                }
            }
        };
        var box = new StackPanel { Spacing = 3 };
        box.Children.Add(new Border { Height = 76, Child = _bankSelector });
        box.Children.Add(new TextBlock { Text = "BANCO ATIVO", FontSize = 10, Foreground = Avalonia.Media.Brush.Parse("#9aa7b5"), HorizontalAlignment = HorizontalAlignment.Center });
        BankSlot.Child = box;
        FillBankSelector();
    }

    private void FillBankSelector()
    {
        if (_bankSelector is null)
        {
            return;
        }

        _fillingBanks = true;
        try
        {
            _bankSelector.Items.Clear();
            string? active = ActiveBank?.Id;
            foreach (ToolBank b in (BanksSource?.Invoke() ?? []).Where(b => b.Machine == ToolBankMachine.Lathe).OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = new ComboBoxItem { Content = b.Name, Tag = b.Id };
                _bankSelector.Items.Add(item);
                if (b.Id == active)
                {
                    _bankSelector.SelectedItem = item;
                }
            }
        }
        finally
        {
            _fillingBanks = false;
        }
    }

    /// <summary>Ao abrir o módulo ou mudar um banco: refaz o seletor do banco ativo e o painel (o programa não muda).</summary>
    public void RefreshBank()
    {
        FillBankSelector();
        ShowWarnings();
        if (SelectedOp() is not null)
        {
            BuildProps();
        }
    }

    /// <summary>
    /// Canal de face e sangramento (TORNO-T2/T3): a largura da ferramenta vem da largura do inserto do bedame escolhido no banco, quando
    /// cadastrada (só leitura do banco). Outras operações e ferramentas sem largura: nada muda.
    /// </summary>
    private static LatheOperationSpec ApplyBankWidth(LatheOperationSpec s, BankTool t) =>
        BankToolTypes.IsParting(t.Type) && t.InsertWidth is { } w && w > 0 ? LatheJobInfo.WithToolWidth(s, w) : s;

    /// <summary>Escolher a ferramenta no banco ativo do torno (só as ativas): copia T, nome e rotação (se cadastrada).</summary>
    private void BankPicker(int i)
    {
        ToolBank? bank = ActiveBank;
        if (bank is null)
        {
            PropsPanel.Children.Add(UiKit.StatusNote("Nenhum banco de torno. Crie um na aba Ferramentas para escolher a ferramenta daqui.", "pending"));
            return;
        }

        IReadOnlyList<BankTool> choices = ToolBankPick.Choices(bank).Select(c => c.Tool).ToList();
        if (choices.Count == 0)
        {
            PropsPanel.Children.Add(new TextBlock { Text = $"O banco \"{bank.Name}\" não tem ferramenta ativa.", Opacity = 0.7, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            return;
        }

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Escolher do banco ativo…" };
        Avalonia.Automation.AutomationProperties.SetName(combo, "Escolher a ferramenta do banco");
        foreach (BankTool t in choices)
        {
            combo.Items.Add(new ComboBoxItem { Content = ToolBankPick.LatheLabel(t) });
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                BankTool t = choices[combo.SelectedIndex];
                Edit(i, x => ApplyBankWidth(ToolBankPick.ApplyLathe(x, t), t), rebuild: true);
            }
        };
        PropsPanel.Children.Add(combo);
    }
}
