namespace CncStudio.Core.Parsing;

/// <summary>Texto curto de "como corrigir" para as mensagens do analisador (mostrado no cartão de avisos).</summary>
public static class DiagnosticAdvice
{
    private static readonly (string Contains, string Advice)[] Known =
    [
        ("Tempo de espera (G04) sem duração", "Escreva o tempo depois do G04, por exemplo G04 P1000 (1 segundo) ou G04 X1."),
        ("sem rotação do fuso", "Defina a rotação (S) e ligue o fuso (M03 ou M04) antes da espera em rotações."),
        ("não é um endereço válido", "Esta letra não existe no comando: confira a digitação. Para variável use #, por exemplo #101."),
        ("não é reconhecido (variável", "Variáveis começam com #; subprogramas são chamados com M98 P…. Confira a grafia."),
        ("Ciclo G", "O simulador ainda não desenha este ciclo; ele é ignorado só na tela. Na máquina o comando segue valendo."),
        ("Ciclo ", "O simulador ainda não desenha este ciclo; ele é ignorado só na tela. Na máquina o comando segue valendo."),
        ("não é simulado (apenas X, Y e Z)", "O desenho mostra só X, Y e Z. O restante não muda o corte simulado; o programa continua válido."),
        ("retorno à referência", "G28/G30 não são desenhados porque a posição da máquina não é conhecida. É normal; confira na máquina."),
        ("Mais de um código de movimento", "Deixe só um G00, G01, G02 ou G03 por linha. Separe em duas linhas."),
        ("repetido no mesmo bloco", "Apague a letra repetida ou separe em duas linhas."),
        ("sem centro", "No arco, informe o centro (I, J ou K, relativo ao início) ou o raio (R)."),
        ("início e fim iguais", "Para o círculo completo use o centro (I/J/K). Com raio (R), o início e o fim precisam ser diferentes."),
        ("raio zero", "O centro do arco está no ponto inicial. Confira I, J e K, ou use R."),
        ("Compensação G41/G42 sem diâmetro", "Cadastre o diâmetro da ferramenta na tabela de ferramentas para a trajetória compensada aparecer."),
        ("sem avanço (F)", "Escreva o avanço (F) antes do primeiro G01, G02 ou G03."),
        ("abaixo de Z0", "Suba em Z antes de mover em XY, ou use G01 com avanço no lugar do G00."),
        ("sem o fechamento correspondente", "Falta fechar o bloco: confira os pares IF/ENDIF e WHILE/ENDWHILE."),
        ("sem END", "Falta o END do laço (DO/END). Confira os números."),
        ("não é suportado e foi ignorado", "O simulador ignora este comando só na tela; o programa continua válido para a máquina."),
        ("ignorado", "O simulador ignorou este trecho. Confira se a letra ou o comando está correto."),
        ("curso", "Algum movimento passa do curso da máquina. Confira os valores de X, Y e Z ou o zero da peça."),
    ];

    /// <summary>Dica de correção para a mensagem; sempre devolve um texto (genérico se não reconhecer).</summary>
    public static string For(string message, DiagnosticSeverity severity)
    {
        foreach ((string contains, string advice) in Known)
        {
            if (message.Contains(contains, StringComparison.OrdinalIgnoreCase))
            {
                return advice;
            }
        }

        return severity == DiagnosticSeverity.Error
            ? "Corrija a linha marcada: na máquina o programa pararia com alarme aqui."
            : "Confira a linha marcada. O simulador seguiu em frente, mas o resultado pode não ser o esperado.";
    }
}
