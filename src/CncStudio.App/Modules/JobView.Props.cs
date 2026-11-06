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
        _fixtureKind = null;
        if ((Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "stock" })
        {
            StockProps();
            return;
        }

        if ((Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "part" })
        {
            PartProps();
            return;
        }

        if ((Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "fixture" })
        {
            FixtureProps();
            return;
        }

        if (SelectedOp() is not { } i || i >= _job.Operations.Count)
        {
            PropsPanel.Children.Add(new TextBlock { Text = "Propriedades", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
            PropsPanel.Children.Add(new TextBlock { Text = "Selecione uma operação na árvore para editar os dados dela.", Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
            return;
        }

        CamOperationSpec s = _job.Operations[i];
        PropsPanel.Children.Add(new TextBlock { Text = "Propriedades", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        PropsPanel.Children.Add(new TextBlock { Text = CamJobInfo.OpTitle(i, s), Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
        if (OpOrigin.Describe(s.Origin) is { } origin)
        {
            // marca de origem (missão 11): sempre com texto; rascunho em âmbar
            PropsPanel.Children.Add(UiKit.StatusNote(origin.Text + " (informado, não verificado)", origin.Kind));
        }

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
        BankButtons(i);
        Text(i, "Nome da ferramenta", s.Tool.Name, "ex.: Fresa topo Z4", (x, v) => x with { Tool = x.Tool with { Name = v.Trim() } });
        Num(i, CamJobInfo.ToolNumberField, "", s.Tool.Number, (x, v) => x with { Tool = x.Tool with { Number = (int)Math.Round(v) } }, check: CamJobInfo.ToolNumberError);
        Num(i, CamJobInfo.DiameterField, "mm", s.Tool.Diameter, (x, v) => x with { Tool = x.Tool with { Diameter = v } });
        Section("Condição de corte");
        Num(i, "Rotação", "rpm", s.Tool.SpindleRpm, (x, v) => x with { Tool = x.Tool with { SpindleRpm = v } }, rebuild: true);
        if (CamJobInfo.SpindleWarning(s.Tool.SpindleRpm, MachineSource?.Invoke()?.MaxSpindleRpm) is { } spindle)
        {
            // só conferência: a rotação e o programa continuam os digitados
            PropsPanel.Children.Add(UiKit.StatusNote(spindle, "pending"));
        }

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
                Pick(i, "Lado", ["Por fora", "Por dentro", "Sobre a linha"], (int)c.Side, (x, n) => x with { ContourParams = x.ContourParams! with { Side = (ContourSide)n } }, rebuild: true);
                Check(i, "Concordante (climb)", c.Climb, (x, b) => x with { ContourParams = x.ContourParams! with { Climb = b } });
                Check(i, "Compensação no controle (G41/G42)", c.ControllerComp, (x, b) => x with { ContourParams = x.ContourParams! with { ControllerComp = b } });
                Num(i, "Sobremetal", "mm", c.Allowance, (x, v) => x with { ContourParams = x.ContourParams! with { Allowance = v } });
                Num(i, "Raio de entrada", "mm", c.LeadRadius, (x, v) => x with { ContourParams = x.ContourParams! with { LeadRadius = v } });
                break;
            case JobOpKind.Pocket when s.PocketParams is { } p:
                Section("Bolsão");
                Num(i, "Passo lateral", "% Ø", p.StepOverPercent, (x, v) => x with { PocketParams = x.PocketParams! with { StepOverPercent = v } });
                Num(i, "Sobremetal de acabamento", "mm", p.FinishAllowance, (x, v) => x with { PocketParams = x.PocketParams! with { FinishAllowance = v } });
                Num(i, "Raio de entrada", "mm", p.LeadRadius, (x, v) => x with { PocketParams = x.PocketParams! with { LeadRadius = v } });
                Check(i, "Contornar ilhas (contornos do desenho dentro deste)", s.Islands, (x, v) => x with { Islands = v });
                break;
            case JobOpKind.Facing:
                Section("Faceamento");
                Num(i, "Passo lateral", "% Ø", s.FacingStepOverPercent, (x, v) => x with { FacingStepOverPercent = v });
                break;
            case JobOpKind.Drilling when s.Drill is { } d:
                Section("Furação");
                Pick(i, "Ciclo", ["G81 furar", "G82 com pausa", "G83 bicando", "G73 quebra-cavaco"], (int)d.Cycle, (x, n) => x with { Drill = x.Drill! with { Cycle = (DrillCycleKind)n } });
                Num(i, CamJobInfo.PeckField, "mm", d.Peck, (x, v) => x with { Drill = x.Drill! with { Peck = v } });
                Num(i, CamJobInfo.DwellField, "s", d.DwellSeconds, (x, v) => x with { Drill = x.Drill! with { DwellSeconds = v } });
                break;
            case JobOpKind.WrapText when s.Wrap is { } w:
                Section("Texto no cilindro (4º eixo contínuo)");
                Text(i, "Texto", w.Text, "", (x, v) => x with { Wrap = x.Wrap! with { Text = v } });
                Num(i, "Altura das letras", "mm", w.LetterHeight, (x, v) => x with { Wrap = x.Wrap! with { LetterHeight = v } });
                Pick(i, "Eixo rotativo", ["A (gira em torno de X)", "B (gira em torno de Y)"], w.Axis == "B" ? 1 : 0, (x, n) => x with { Wrap = x.Wrap! with { Axis = n == 1 ? "B" : "A" } });
                Num(i, "Diâmetro da peça", "mm", w.Setup.Diameter, (x, v) => x with { Wrap = x.Wrap! with { Setup = x.Wrap!.Setup with { Diameter = v } } });
                Num(i, "Início ao longo do eixo", "mm", w.Setup.StartAxial, (x, v) => x with { Wrap = x.Wrap! with { Setup = x.Wrap!.Setup with { StartAxial = v } } });
                Num(i, "Ângulo inicial", "°", w.Setup.StartAngle, (x, v) => x with { Wrap = x.Wrap! with { Setup = x.Wrap!.Setup with { StartAngle = v } } });
                Num(i, "Profundidade", "mm", w.Setup.Depth, (x, v) => x with { Wrap = x.Wrap! with { Setup = x.Wrap!.Setup with { Depth = v } } });
                Num(i, "Passe em profundidade", "mm", w.Setup.PassDepth, (x, v) => x with { Wrap = x.Wrap! with { Setup = x.Wrap!.Setup with { PassDepth = v } } });
                Num(i, "Folga acima da peça", "mm", w.Setup.Clearance, (x, v) => x with { Wrap = x.Wrap! with { Setup = x.Wrap!.Setup with { Clearance = v } } });
                Num(i, "Avanço de mergulho", "mm/min", w.Setup.PlungeFeed, (x, v) => x with { Wrap = x.Wrap! with { Setup = x.Wrap!.Setup with { PlungeFeed = v } } });
                break;
        }

        if (s.Kind != JobOpKind.WrapText)
        {
            Section("4º eixo e 3+2");
            Check(i, "Face girada (perfil da máquina)", s.Face is not null, (x, b) => WithoutFaceMissing(x) with { Face = b ? new RotaryFaceSpec("A", 90, 25) : null }, rebuild: true);
            MachineProfile? machine = MachineSource?.Invoke();
            bool tableTable = machine?.Configuration == MachineConfiguration.FiveAxisTableTable;
            if (s.Face is { Axis2: not null } || (s.Face is not null && tableTable))
            {
                // 3+2 só se oferece com perfil de mesa dupla; uma face 3+2 já salva aparece sempre (o erro do perfil vem no percurso)
                Check(i, "3+2: usar os dois eixos da mesa", s.Face?.Axis2 is not null, (x, b) => WithoutFaceMissing(x) with
                {
                    Face = b && machine is { Axes.Count: 2 } m
                        ? x.Face! with { Axis = m.Axes[0].Letter, Axis2 = m.Axes[1].Letter, Angle2 = 0 }
                        : x.Face! with { Axis = x.Face!.Axis is "A" or "B" ? x.Face.Axis : "A", Axis2 = null, Angle2 = 0 },
                }, rebuild: true);
            }

            IReadOnlyList<string> faceFields = CamJobInfo.FaceFields(s.Face);
            if (s.Face is { Axis2: { } axis2 } t)
            {
                PropsPanel.Children.Add(new TextBlock { Text = $"Basculante {t.Axis} carrega o giratório {axis2} (ordem do perfil da máquina).", Opacity = 0.65, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                Num(i, faceFields[0], "°", t.Angle, (x, v) => x with { Face = x.Face! with { Angle = v } });
                Num(i, faceFields[1], "°", t.Angle2, (x, v) => x with { Face = x.Face! with { Angle2 = v } });
                Num(i, faceFields[2], "mm", t.AxisToFace, (x, v) => x with { Face = x.Face! with { AxisToFace = v } });
                PropsPanel.Children.Add(new TextBlock { Text = $"Desenhe a face de cima (Z 0 na face), como fica com os dois giros. O programa gira {axis2} e depois {t.Axis} na altura segura; os cursos vêm do perfil da máquina.", Opacity = 0.65, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                PropsPanel.Children.Add(UiKit.StatusNote("⚠ " + RotaryIndexing.NotMachineVerified + " Colisão da mesa e da fixação nos giros não é verificada.", "pending"));
            }
            else if (s.Face is { } f)
            {
                Pick(i, "Eixo", ["A", "B"], f.Axis == "B" ? 1 : 0, (x, n) => x with { Face = x.Face! with { Axis = n == 1 ? "B" : "A" } });
                Num(i, faceFields[0], "°", f.Angle, (x, v) => x with { Face = x.Face! with { Angle = v } });
                Num(i, faceFields[1], "mm", f.AxisToFace, (x, v) => x with { Face = x.Face! with { AxisToFace = v } });
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
        CamOperationSpec old = _job.Operations[i];
        _job.Operations[i] = OpOrigin.Track(old, change(old));
        CamTool after = _job.Operations[i].Tool;
        if (after.Number == before.Number && (after.Diameter != before.Diameter || after.Name != before.Name))
        {
            // Geometria da ferramenta vale para todo o mesmo T; rotação e avanços ficam em cada operação
            _job.ApplyToolGeometry(after.Number, after.Diameter, after.Name);
        }

        Refresh(op: i, props: rebuild);
    }

    /// <summary>Tira da operação os campos vazios da face girada (a face foi ligada, desligada ou trocou de modo: os campos são outros).</summary>
    private static CamOperationSpec WithoutFaceMissing(CamOperationSpec x)
    {
        IReadOnlyList<string> face = CamJobInfo.FaceFields(x.Face);
        return x with { MissingFields = x.MissingFields.Where(f => !face.Contains(f)).ToList() };
    }

    /// <summary>
    /// Campo numérico da operação. Vazio ou inválido (texto, ou o que <paramref name="check"/> recusar) marca o campo na operação
    /// (<see cref="CamJobInfo.SetMissing"/>): ela fica Incompleta, não gera e o número antigo não é usado nem mostrado (CAM-12).
    /// Um valor válido grava e tira a marca.
    /// </summary>
    private void Num(int i, string label, string unit, double value, Func<CamOperationSpec, double, CamOperationSpec> apply, bool rebuild = false, Func<double, string?>? check = null)
    {
        bool missing = i < _job.Operations.Count && _job.Operations[i].MissingFields.Contains(label);
        var box = new TextBox { Text = missing ? "" : Fmt(value), MinHeight = 26, Watermark = "sem valor" };
        void Mark(string? why)
        {
            if (why is null)
            {
                box.ClearValue(TemplatedControl.ForegroundProperty);
                box.ClearValue(TemplatedControl.BorderBrushProperty);
                box.ClearValue(TemplatedControl.BorderThicknessProperty);
                ToolTip.SetTip(box, null);
                return;
            }

            box.Foreground = Avalonia.Media.Brushes.IndianRed;
            box.BorderBrush = Avalonia.Media.Brushes.IndianRed;
            box.BorderThickness = new Avalonia.Thickness(2);
            ToolTip.SetTip(box, why);
        }

        if (missing)
        {
            Mark($"Falta {label}: digite um valor. O valor antigo não é usado.");
        }

        void Commit()
        {
            string typed = (box.Text ?? "").Trim();
            double v = ParseNum(typed);
            string? why = typed.Length == 0 ? $"Falta {label}: digite um valor."
                : double.IsNaN(v) ? $"\"{typed}\" não é um número. Falta {label}."
                : check?.Invoke(v);
            if (why is not null)
            {
                Mark(why);
                if (!missing)
                {
                    missing = true;
                    Edit(i, x => CamJobInfo.SetMissing(x, label, true), rebuild);
                }

                Errors.Text = "• " + why;
                return;
            }

            Mark(null);
            if (missing || Math.Abs(v - value) > 1e-9)
            {
                missing = false;
                value = v;
                Edit(i, x => CamJobInfo.SetMissing(apply(x, v), label, false), rebuild);
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

    // ---------------------------------------------------------------- peça (forma sem DXF)

    private readonly Dictionary<string, string> _shapeText = [];

    /// <summary>
    /// Desenhar forma sem DXF (migrado da aba Fresa antiga): retângulo e círculo, com os mesmos campos e valores iniciais de lá.
    /// Cada forma entra no fim da lista de contornos, então as operações que já apontam para contornos não mudam.
    /// </summary>
    private void PartProps()
    {
        PropsPanel.Children.Add(new TextBlock { Text = "Peça", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        PropsPanel.Children.Add(new TextBlock
        {
            Text = _job.Contours.Count == 0
                ? "Sem desenho. Abra um DXF ou desenhe uma forma abaixo."
                : $"{_job.Contours.Count} contorno(s). A forma desenhada entra no fim da lista.",
            Opacity = 0.75, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var error = new ContentControl();

        // os campos guardam o último valor digitado (como na aba antiga), mesmo depois de refazer o painel
        var fields = new List<(string Label, TextBox Box)>();
        TextBox Field(string label, double value)
        {
            var b = new TextBox { Text = _shapeText.GetValueOrDefault(label, Fmt(value)), MinHeight = 26 };
            fields.Add((label, b));
            PropsPanel.Children.Add(Row(label, b, "mm"));
            return b;
        }

        // o Desfazer cobre só operações: a forma fica (como na aba antiga, que também não desfazia)
        void Added()
        {
            foreach ((string label, TextBox box) in fields)
            {
                _shapeText[label] = box.Text ?? "";
            }

            Refresh(keep: true);
        }

        PropsPanel.Children.Add(new TextBlock { Text = "Retângulo", FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Avalonia.Thickness(0, 8, 0, 0) });
        TextBox rx = Field("Canto X", 0), ry = Field("Canto Y", 0), rw = Field("Largura", 60), rh = Field("Altura", 40);
        var addRect = new Button { Content = "Adicionar retângulo", Padding = new Avalonia.Thickness(10, 5) };
        addRect.Click += (_, _) =>
        {
            double x = ParseNum(rx.Text), y = ParseNum(ry.Text), w = ParseNum(rw.Text), h = ParseNum(rh.Text);
            if (CamJobInfo.RectangleError(x, y, w, h) is { } bad)
            {
                error.Content = UiKit.StatusNote("✕ inválido: " + bad, "bad");
                return;
            }

            _job.Contours.Add(Contour.Rectangle(x, y, w, h));
            Added();
        };
        PropsPanel.Children.Add(addRect);

        PropsPanel.Children.Add(new TextBlock { Text = "Círculo", FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Avalonia.Thickness(0, 8, 0, 0) });
        TextBox cx = Field("Centro X", 30), cy = Field("Centro Y", 20), cd = Field("Diâmetro", 20);
        var addCircle = new Button { Content = "Adicionar círculo", Padding = new Avalonia.Thickness(10, 5) };
        addCircle.Click += (_, _) =>
        {
            double x = ParseNum(cx.Text), y = ParseNum(cy.Text), d = ParseNum(cd.Text);
            if (CamJobInfo.CircleError(x, y, d) is { } bad)
            {
                error.Content = UiKit.StatusNote("✕ inválido: " + bad, "bad");
                return;
            }

            _job.Contours.Add(Contour.Circle(new Point2(x, y), d / 2));
            Added();
        };
        PropsPanel.Children.Add(addCircle);
        PropsPanel.Children.Add(error);
    }

    // ---------------------------------------------------------------- bruto

    /// <summary>
    /// Editor do bruto (UX-01 rodada 3): comprimento, largura, altura e canto, nos campos que o Trabalho já tem (<see cref="JobStock"/>).
    /// Sem bruto, as medidas vêm do desenho e a altura fica vazia: só vale depois de "Definir bruto".
    /// </summary>
    private void StockProps()
    {
        PropsPanel.Children.Add(new TextBlock { Text = "Bruto", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        JobStock? st = _job.Stock;
        (double X, double Y, double Width, double Length)? box = st is { } s0 ? (s0.X, s0.Y, s0.Width, s0.Length) : CamJobInfo.DrawingBox(_job.Contours);
        if (st is null)
        {
            PropsPanel.Children.Add(UiKit.StatusNote(
                box is null ? "Bruto não definido. Digite as medidas e clique em Definir bruto." : "Bruto não definido. Medidas preenchidas pelo desenho: confira, digite a altura e clique em Definir bruto.",
                "pending"));
        }

        TextBox Field(string label, string unit, double? value)
        {
            var b = new TextBox { Text = value is { } v ? Fmt(v) : "", MinHeight = 26 };
            PropsPanel.Children.Add(Row(label, b, unit));
            return b;
        }

        TextBox width = Field("Comprimento (X)", "mm", box?.Width);
        TextBox length = Field("Largura (Y)", "mm", box?.Length);
        TextBox height = Field("Altura (Z)", "mm", st?.Thickness);
        TextBox x = Field("Canto X", "mm", box?.X ?? 0);
        TextBox y = Field("Canto Y", "mm", box?.Y ?? 0);
        PropsPanel.Children.Add(new TextBlock
        {
            Text = "Topo do bruto em Z0 (desce até Z −altura). O Z topo de cada operação continua nas propriedades dela. O bruto ainda não altera o programa gerado.",
            Opacity = 0.7, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var error = new ContentControl();
        var apply = new Button { Content = "Definir bruto", Padding = new Avalonia.Thickness(12, 5), FontWeight = Avalonia.Media.FontWeight.SemiBold };
        apply.Click += (_, _) =>
        {
            double w = ParseNum(width.Text), l = ParseNum(length.Text), h = ParseNum(height.Text), cx = ParseNum(x.Text), cy = ParseNum(y.Text);
            string? problem = CamJobInfo.StockError(w, l, h) ?? (double.IsNaN(cx) || double.IsNaN(cy) ? "Confira o canto X e Y do bruto." : null);
            if (problem is not null)
            {
                error.Content = UiKit.StatusNote("✕ inválido: " + problem, "bad");
                return;
            }

            _job.Stock = new JobStock(cx, cy, w, l, h);
            Refresh(keep: true);
        };
        PropsPanel.Children.Add(apply);
        PropsPanel.Children.Add(error);
    }

    // ---------------------------------------------------------------- fixação

    /// <summary>Campo do tipo de fixação aberto nas propriedades (a barra leva o foco até ele).</summary>
    private ComboBox? _fixtureKind;

    /// <summary>
    /// Editor da fixação (UX-01 rodada 3): tipo e descrição curta. É só registro do que o usuário informa; o app não confere
    /// fixação nem colisão. Fonte única: o Assist lê daqui.
    /// </summary>
    private void FixtureProps()
    {
        PropsPanel.Children.Add(new TextBlock { Text = "Fixação", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        JobFixture? fx = _job.Fixture;
        PropsPanel.Children.Add(UiKit.StatusNote(
            fx is null
                ? "Fixação não definida. Escolha como a peça fica presa e clique em Definir fixação."
                : "Fixação informada por você e não verificada: o app não confere morsa, grampos, vácuo nem colisão.",
            fx is null ? "pending" : "neutral"));

        var kind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Escolha o tipo…" };
        foreach ((_, string name) in JobFixture.Kinds)
        {
            kind.Items.Add(new ComboBoxItem { Content = name });
        }

        kind.SelectedIndex = fx is null ? -1 : JobFixture.Kinds.ToList().FindIndex(k => k.Code == fx.Kind);
        _fixtureKind = kind;
        // rótulo em cima e campo na largura toda: o nome do tipo e a descrição não cabem na coluna estreita dos números
        PropsPanel.Children.Add(new TextBlock { Text = "Tipo", FontSize = 12 });
        PropsPanel.Children.Add(kind);
        var description = new TextBox { Text = fx?.Description ?? "", Watermark = "ex.: morsa de 150 mm, mordente liso", MinHeight = 26, MaxLength = JobFixture.MaxDescription };
        PropsPanel.Children.Add(new TextBlock { Text = "Descrição", FontSize = 12 });
        PropsPanel.Children.Add(description);
        PropsPanel.Children.Add(new TextBlock
        {
            Text = "Opcional, até " + JobFixture.MaxDescription + " letras (obrigatória no tipo \"Outro\").",
            Opacity = 0.7, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var error = new ContentControl();
        var apply = new Button { Content = "Definir fixação", Padding = new Avalonia.Thickness(12, 5), FontWeight = Avalonia.Media.FontWeight.SemiBold };
        apply.Click += (_, _) =>
        {
            if (kind.SelectedIndex < 0)
            {
                error.Content = UiKit.StatusNote("✕ inválido: escolha o tipo de fixação.", "bad");
                return;
            }

            var chosen = new JobFixture(JobFixture.Kinds[kind.SelectedIndex].Code, (description.Text ?? "").Trim());
            if (chosen.Validate() is { } problem)
            {
                error.Content = UiKit.StatusNote("✕ inválido: " + problem + ".", "bad");
                return;
            }

            _job.Fixture = chosen;
            Refresh(keep: true);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(apply);
        if (fx is not null)
        {
            var clear = new Button { Content = "Limpar", Padding = new Avalonia.Thickness(12, 5) };
            clear.Click += (_, _) =>
            {
                _job.Fixture = null;
                Refresh(keep: true);
            };
            row.Children.Add(clear);
        }

        PropsPanel.Children.Add(row);
        PropsPanel.Children.Add(error);
    }

    // ---------------------------------------------------------------- banco de ferramentas

    /// <summary>
    /// Salvar e excluir a ferramenta da operação no banco (migrado da aba Fresa antiga). Salvar grava ou atualiza pelo T; excluir pede
    /// confirmação na própria tela (a aba antiga apagava direto). Ferramenta de torno com o mesmo T não é tocada.
    /// </summary>
    private void BankButtons(int i)
    {
        List<ToolEntry>? bank = ToolsSource?.Invoke();
        if (bank is null)
        {
            return;
        }

        CamTool tool = _job.Operations[i].Tool;
        ToolEntry? existing = bank.FirstOrDefault(t => t.Number == tool.Number);
        var message = new ContentControl();
        var row = new WrapPanel();
        var save = new Button { Content = existing is null ? $"Salvar T{tool.Number} no banco" : $"Atualizar T{tool.Number} no banco", Padding = new Avalonia.Thickness(10, 4), Margin = new Avalonia.Thickness(0, 0, 6, 6), FontSize = 12 };
        save.Click += (_, _) =>
        {
            if (CamJobInfo.BankToolError(tool.Number, tool.Diameter) is { } bad)
            {
                message.Content = UiKit.StatusNote("✕ inválido: " + bad, "bad");
                return;
            }

            ToolEntry? entry = bank.FirstOrDefault(t => t.Number == tool.Number);
            if (entry is not null && !IsMillTool(entry))
            {
                message.Content = UiKit.StatusNote($"✕ inválido: T{tool.Number} do banco é ferramenta de torno; use outro número.", "bad");
                return;
            }

            if (entry is null)
            {
                entry = new ToolEntry { Number = tool.Number };
                bank.Add(entry);
            }

            entry.Diameter = tool.Diameter;
            entry.Description = tool.Name.Trim();
            entry.SpindleRpm = Math.Max(0, tool.SpindleRpm);
            entry.FeedXY = Math.Max(0, tool.FeedXY);
            entry.FeedZ = Math.Max(0, tool.FeedZ);
            SaveTools?.Invoke();
            BuildProps();
        };
        row.Children.Add(save);

        if (existing is not null && IsMillTool(existing))
        {
            var delete = new Button { Content = $"Excluir T{tool.Number} do banco…", Padding = new Avalonia.Thickness(10, 4), Margin = new Avalonia.Thickness(0, 0, 6, 6), FontSize = 12 };
            delete.Click += (_, _) =>
            {
                // confirmação na tela: diz o que sai e que a operação continua com a ferramenta
                var confirm = new Button { Content = $"Sim, excluir T{tool.Number}", Padding = new Avalonia.Thickness(10, 4), Margin = new Avalonia.Thickness(0, 6, 6, 0) };
                var cancel = new Button { Content = "Cancelar", Padding = new Avalonia.Thickness(10, 4), Margin = new Avalonia.Thickness(0, 6, 0, 0) };
                var panel = new StackPanel();
                panel.Children.Add(UiKit.StatusNote($"Excluir T{tool.Number} (Ø{Fmt(existing.Diameter)}) do banco? A operação continua com a ferramenta; o simulador do Code deixa de achar T{tool.Number} no banco.", "pending"));
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                buttons.Children.Add(confirm);
                buttons.Children.Add(cancel);
                panel.Children.Add(buttons);
                message.Content = panel;
                cancel.Click += (_, _) => message.Content = null;
                confirm.Click += (_, _) =>
                {
                    bank.RemoveAll(t => t.Number == tool.Number && IsMillTool(t));
                    SaveTools?.Invoke();
                    BuildProps();
                };
            };
            row.Children.Add(delete);
        }

        PropsPanel.Children.Add(row);
        PropsPanel.Children.Add(message);
    }

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
            Edit(i, x =>
            {
                // o banco preenche T e Ø (e rotação e avanços quando tem): esses deixam de estar vazios; o resto segue como está
                CamOperationSpec y = CamJobInfo.SetMissing(CamJobInfo.SetMissing(x, CamJobInfo.ToolNumberField, false), CamJobInfo.DiameterField, false);
                y = t.SpindleRpm > 0 ? CamJobInfo.SetMissing(y, "Rotação", false) : y;
                y = t.FeedXY > 0 ? CamJobInfo.SetMissing(CamJobInfo.SetMissing(y, "Avanço no plano", false), "Avanço na superfície", false) : y;
                y = t.FeedZ > 0 ? CamJobInfo.SetMissing(y, "Avanço de descida", false) : y;
                return y with
                {
                    Tool = new CamTool(t.Number, t.Diameter, t.SpindleRpm > 0 ? t.SpindleRpm : x.Tool.SpindleRpm, t.FeedXY > 0 ? t.FeedXY : x.Tool.FeedXY, t.FeedZ > 0 ? t.FeedZ : x.Tool.FeedZ, t.Description),
                };
            }, rebuild: true);
        };
        PropsPanel.Children.Add(combo);
    }
}
