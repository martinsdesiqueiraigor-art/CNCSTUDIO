using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>
/// Como a peça fica presa no torno: castanhas, placa ou entre pontas (castanhas + contraponto); castanha dura e castanha mole
/// (TORNO-T4) vêm depois para não mudar os nomes antigos.
/// </summary>
public enum LatheFixationKind { Jaws, Faceplate, Center, HardJaws, SoftJaws }

/// <summary>
/// Fixação do Trabalho de torno, só informativa: o app nunca verifica se a peça está presa nem se há colisão com ela.
/// Medidas opcionais (null = não informada): Ø de fixação, comprimento preso e balanço livre, em mm.
/// <see cref="Tailstock"/> e <see cref="SteadyRest"/> (TORNO-T4): apoios marcados pelo usuário, também só informativos.
/// </summary>
public sealed record LatheFixation(LatheFixationKind Kind, double? GripDiameter = null, double? GripLength = null, double? StickOut = null)
{
    /// <summary>Contraponto apoiando a ponta da peça (informativo).</summary>
    public bool Tailstock { get; init; }

    /// <summary>Luneta apoiando o meio da peça (informativo).</summary>
    public bool SteadyRest { get; init; }

    /// <summary>Nomes da tela, na ordem do <see cref="LatheFixationKind"/>.</summary>
    public static readonly IReadOnlyList<string> KindNames = ["Castanhas", "Placa", "Entre pontas (castanhas + contraponto)", "Castanha dura", "Castanha mole"];

    /// <summary>Nome gravado no arquivo para cada tipo (o mesmo mostrado na tela).</summary>
    public static string FileName(LatheFixationKind k) => k switch
    {
        LatheFixationKind.Jaws => "castanhas",
        LatheFixationKind.Faceplate => "placa",
        LatheFixationKind.HardJaws => "castanha-dura",
        LatheFixationKind.SoftJaws => "castanha-mole",
        _ => "ponto",
    };

    /// <summary>Tipo a partir do nome do arquivo (null = desconhecido).</summary>
    public static LatheFixationKind? FromFileName(string name) => name switch
    {
        "castanhas" => LatheFixationKind.Jaws,
        "placa" => LatheFixationKind.Faceplate,
        "ponto" => LatheFixationKind.Center,
        "castanha-dura" => LatheFixationKind.HardJaws,
        "castanha-mole" => LatheFixationKind.SoftJaws,
        _ => null,
    };

    /// <summary>Medidas informadas precisam ser números maiores que zero (null = válida).</summary>
    public string? Validate()
    {
        static bool Bad(double? v) => v is { } x && !(double.IsFinite(x) && x > 0);
        if (Bad(GripDiameter))
        {
            return "O Ø de fixação precisa ser maior que zero (ou deixe em branco).";
        }

        if (Bad(GripLength))
        {
            return "O comprimento preso precisa ser maior que zero (ou deixe em branco).";
        }

        return Bad(StickOut) ? "O balanço livre precisa ser maior que zero (ou deixe em branco)." : null;
    }

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(double v) => v.ToString("0.##", Br);

    /// <summary>Linha "Fixação" da árvore: "Fixação: castanhas · Ø40 · preso 20 mm · balanço 60 mm" ou "Fixação: não informada".</summary>
    public static string Title(LatheFixation? f)
    {
        if (f is null)
        {
            return "Fixação: não informada";
        }

        string kind = f.Kind switch
        {
            LatheFixationKind.Center => "entre pontas",
            LatheFixationKind.HardJaws => "castanha dura",
            LatheFixationKind.SoftJaws => "castanha mole",
            _ => FileName(f.Kind),
        };
        var parts = new List<string> { "Fixação: " + kind };
        if (f.GripDiameter is { } d)
        {
            parts.Add("Ø" + N(d));
        }

        if (f.GripLength is { } l)
        {
            parts.Add($"preso {N(l)} mm");
        }

        if (f.StickOut is { } s)
        {
            parts.Add($"balanço {N(s)} mm");
        }

        if (f.Tailstock)
        {
            parts.Add("contraponto");
        }

        if (f.SteadyRest)
        {
            parts.Add("luneta");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Estado na árvore: inválida (vermelho) com medida errada; senão sempre "não verificada" (âmbar), informada ou não.</summary>
    public static (string Kind, string Text) State(LatheFixation? f) =>
        f?.Validate() is { } error ? ("bad", "erro: " + error) : ("pending", "não verificada");
}
