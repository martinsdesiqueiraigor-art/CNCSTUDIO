using System.Globalization;
using System.Text.RegularExpressions;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Lexing;

namespace CncStudio.Core.Editing;

/// <summary>
/// Explicação curta (em português) do código sob o cursor: G/M, endereços, ciclos com os
/// parâmetros, palavras-chave e variáveis. Usada na dica que aparece ao parar o mouse no editor.
/// Indica também o que o CncStudio não desenha, para ninguém achar que o desenho está completo.
/// </summary>
public static partial class GCodeHelp
{
    private const string NotDrawn = "\n(Aceito, mas não aparece no desenho.)";
    private const string NotSupported = "\n(Não suportado pelo CncStudio: é ignorado, com aviso.)";

    // ───────────────────────── Códigos G ─────────────────────────

    private static readonly Dictionary<int, string> CommonG = new()
    {
        [0] = "G00 — Avanço rápido (posicionamento). Não corta; desenhado tracejado em vermelho.",
        [1] = "G01 — Interpolação linear com o avanço F.",
        [2] = "G02 — Arco no sentido horário.\nCentro por I/J/K (incremental a partir do início) ou raio R (CR= no Siemens).\nR negativo = arco maior que 180°.",
        [3] = "G03 — Arco no sentido anti-horário.\nCentro por I/J/K (incremental a partir do início) ou raio R (CR= no Siemens).\nR negativo = arco maior que 180°.",
        [17] = "G17 — Plano XY (arcos e compensação em XY). Padrão da fresa.",
        [18] = "G18 — Plano ZX. Padrão do torno.",
        [19] = "G19 — Plano YZ.",
        [40] = "G40 — Cancela a compensação de raio da ferramenta.",
        [41] = "G41 — Compensação de raio à esquerda do contorno (no sentido do movimento).\nUsa o diâmetro da tabela de ferramentas.",
        [42] = "G42 — Compensação de raio à direita do contorno (no sentido do movimento).\nUsa o diâmetro da tabela de ferramentas.",
        [53] = "G53 — Movimento em coordenadas da máquina (só neste bloco)." + NotDrawn,
        [54] = "G54 — Zero de peça 1 (tabela em Máquina → Ferramentas e zeros de peça).",
        [55] = "G55 — Zero de peça 2.",
        [56] = "G56 — Zero de peça 3.",
        [57] = "G57 — Zero de peça 4.",
        [58] = "G58 — Zero de peça 5.",
        [59] = "G59 — Zero de peça 6.",
        [96] = "G96 — Velocidade de corte constante: S passa a ser Vc (m/min).\nA rotação muda com o diâmetro, até o limite (G50 S no Fanuc, LIMS= no Siemens).",
        [97] = "G97 — Rotação constante: S em rpm.",
    };

    private static readonly Dictionary<int, string> FanucG = new()
    {
        [4] = "G04 — Tempo de espera: G04 X1.5 (segundos) ou G04 P1500 (milissegundos).",
        [10] = "G10 — Grava dados por programa (corretores, zeros de peça)." + NotDrawn,
        [20] = "G20 — Programação em polegadas.",
        [21] = "G21 — Programação em milímetros.",
        [28] = "G28 — Retorno à referência da máquina (passando pelo ponto programado)." + NotDrawn,
        [30] = "G30 — Retorno à 2ª/3ª/4ª referência." + NotDrawn,
        [43] = "G43 — Compensação de comprimento da ferramenta (+), com H." + NotDrawn,
        [44] = "G44 — Compensação de comprimento da ferramenta (−), com H." + NotDrawn,
        [49] = "G49 — Cancela a compensação de comprimento.",
        [52] = "G52 — Sistema de coordenadas local." + NotSupported,
        [65] = "G65 — Chamada de macro com argumentos: G65 P(programa) A.. B.. ...\nAs letras viram variáveis locais: A=#1 B=#2 C=#3 I=#4 J=#5 K=#6 D=#7 E=#8 F=#9 H=#11\nM=#13 Q=#17 R=#18 S=#19 T=#20 U=#21 V=#22 W=#23 X=#24 Y=#25 Z=#26.",
        [66] = "G66 — Chamada modal de macro (repete a cada movimento até G67).",
        [67] = "G67 — Cancela a chamada modal G66.",
        [68] = "G68 — Rotação de coordenadas." + NotSupported,
        [69] = "G69 — Cancela a rotação de coordenadas.",
        [80] = "G80 — Cancela o ciclo de furação.",
        [81] = "G81 — Furação simples: G81 X.. Y.. Z(fundo) R(plano R) F..\nRepete em cada posição XY até G80.",
        [82] = "G82 — Furação com espera no fundo: G82 X.. Y.. Z.. R.. P(ms) F..",
        [83] = "G83 — Furação profunda em bicadas: G83 X.. Y.. Z.. R.. Q(bicada) F..\nRecua até o plano R depois de cada bicada.",
        [84] = "G84 — Macho (rosca à direita)." + NotSupported,
        [85] = "G85 — Mandrilamento (avanço na ida e na volta)." + NotSupported,
        [86] = "G86 — Mandrilamento com parada do fuso." + NotSupported,
        [98] = "G98 — Ciclos de furação: volta ao nível inicial depois de cada furo.",
        [99] = "G99 — Ciclos de furação: volta ao plano R depois de cada furo.",
        [90] = "G90 — Coordenadas absolutas.",
        [91] = "G91 — Coordenadas incrementais.",
        [92] = "G92 — Define o sistema de coordenadas pela posição atual." + NotSupported,
        [94] = "G94 — Avanço em mm/min.",
        [95] = "G95 — Avanço em mm por rotação.",
        [73] = "G73 — Furação profunda de alta velocidade (quebra-cavaco)." + NotSupported,
        [74] = "G74 — Macho à esquerda." + NotSupported,
        [76] = "G76 — Mandrilamento fino." + NotSupported,
    };

    /// <summary>Fanuc torno (sistema A): códigos com significado diferente da fresa.</summary>
    private static readonly Dictionary<int, string> FanucLatheG = new()
    {
        [32] = "G32 — Rosca em um passe: G32 Z(W).. F(passo).",
        [50] = "G50 — Torno: G50 S.. limita a rotação máxima (para G96);\nG50 X.. Z.. define a posição atual como esse ponto.",
        [70] = "G70 — Acabamento: G70 P(ns) Q(nf).\nSegue o perfil dos blocos N ns a N nf com o F/S do perfil e volta ao ponto inicial.",
        [71] = "G71 — Desbaste longitudinal (torno), dois blocos:\nG71 U(Δd profundidade, raio) R(e recuo)\nG71 P(ns) Q(nf) U(Δu sobremetal X, diâmetro) W(Δw sobremetal Z) F..\nAceita perfil com rebaixo (tipo II). Depois do ciclo, segue no bloco após N nf.",
        [72] = "G72 — Desbaste de face (torno), dois blocos:\nG72 W(Δd profundidade) R(e recuo)\nG72 P(ns) Q(nf) U(Δu) W(Δw) F..",
        [73] = "G73 — Repetição de padrão (torno)." + NotSupported,
        [74] = "G74 — Furação/canal de face em bicadas (torno), dois blocos:\nG74 R(e recuo)\nG74 X(U).. Z(W).. P(Δi deslocamento em X, µm) Q(Δk bicada em Z, µm) R(Δd) F..",
        [75] = "G75 — Canal em X com bicadas (torno), dois blocos:\nG75 R(e recuo)\nG75 X(U).. Z(W).. P(Δi bicada em X, µm) Q(Δk deslocamento em Z, µm) R(Δd) F..",
        [76] = "G76 — Rosca em vários passes (torno), dois blocos:\nG76 P(m passes de acabamento)(r chanfro)(a ângulo) Q(Δdmin, µm) R(d sobremetal)\nG76 X(U)(raiz) Z(W)(fim) R(i conicidade) P(k altura do filete, µm) Q(Δd 1º passe, µm) F(passo)",
        [90] = "G90 — Torno: ciclo de torneamento: G90 X(U).. Z(W).. R(conicidade) F..\nRepete a cada novo X/Z até G00/G01.",
        [92] = "G92 — Torno: ciclo de rosca simples: G92 X(U).. Z(W).. R(conicidade) F(passo).",
        [94] = "G94 — Torno: ciclo de faceamento: G94 X(U).. Z(W).. R(conicidade) F..",
        [98] = "G98 — Torno: avanço em mm/min.",
        [99] = "G99 — Torno: avanço em mm por rotação (padrão do torno).",
        [81] = "G81 — Ciclo de furação no torno." + NotSupported,
        [83] = "G83 — Ciclo de furação no torno." + NotSupported,
    };

    private static readonly Dictionary<int, string> SiemensG = new()
    {
        [4] = "G4 — Tempo de espera: G4 F2 (segundos) ou G4 S10 (rotações do fuso).",
        [9] = "G9 — Parada exata neste bloco." + NotDrawn,
        [25] = "G25 — Limite mínimo de rotação (com S)." + NotDrawn,
        [26] = "G26 — Limite máximo de rotação (com S).",
        [33] = "G33 — Rosca: G33 Z.. K(passo).",
        [60] = "G60 — Parada exata (modal)." + NotDrawn,
        [64] = "G64 — Modo contínuo (arredonda cantos na máquina)." + NotDrawn,
        [70] = "G70 — Programação em polegadas.",
        [71] = "G71 — Programação em milímetros.",
        [90] = "G90 — Coordenadas absolutas (por eixo: AC(...)).",
        [91] = "G91 — Coordenadas incrementais (por eixo: IC(...)).",
        [94] = "G94 — Avanço em mm/min.",
        [95] = "G95 — Avanço em mm por rotação.",
        [500] = "G500 — Desliga o zero de peça ajustável.",
        [505] = "G505 — Zero de peça ajustável 5." + NotSupported,
        [110] = "G110 — Polo em relação à última posição." + NotSupported,
        [111] = "G111 — Polo em relação ao zero." + NotSupported,
        [112] = "G112 — Polo em relação ao polo anterior." + NotSupported,
        [641] = "G641 — Modo contínuo com arredondamento." + NotDrawn,
        [642] = "G642 — Modo contínuo com tolerância." + NotDrawn,
    };

    // ───────────────────────── Códigos M ─────────────────────────

    private static readonly Dictionary<int, string> CommonM = new()
    {
        [0] = "M00 — Parada programada.",
        [1] = "M01 — Parada opcional (só com a chave de parada opcional ligada).",
        [2] = "M02 — Fim de programa.",
        [3] = "M03 — Liga o fuso no sentido horário.",
        [4] = "M04 — Liga o fuso no sentido anti-horário.",
        [5] = "M05 — Para o fuso.",
        [6] = "M06 — Troca de ferramenta (pela T selecionada).",
        [7] = "M07 — Refrigeração por névoa.",
        [8] = "M08 — Liga a refrigeração.",
        [9] = "M09 — Desliga a refrigeração.",
        [17] = "M17 — Fim de subprograma (volta para quem chamou).",
        [19] = "M19 — Orientação do fuso.",
        [30] = "M30 — Fim de programa e volta ao início.",
    };

    private static readonly Dictionary<int, string> FanucM = new()
    {
        [98] = "M98 — Chama subprograma: M98 P(número) L(repetições), ou M98 P0031234 (3× o O1234).\nProcura no mesmo arquivo e na pasta do programa (O1234.nc...).",
        [99] = "M99 — Fim de subprograma (volta para quem chamou). M99 P.. volta para o bloco N indicado.",
    };

    // ───────────────────────── Endereços ─────────────────────────

    private static string? Address(char letter, Dialect dialect, MachineKind machine)
    {
        bool lathe = machine == MachineKind.Lathe;
        bool fanuc = dialect == Dialect.Fanuc;
        return letter switch
        {
            'N' => "N — Número do bloco (rótulo para saltos e para P/Q dos ciclos).",
            'X' => lathe ? "X — Eixo X, em diâmetro (torno)." : "X — Eixo X.",
            'Y' => "Y — Eixo Y.",
            'Z' => "Z — Eixo Z.",
            'U' when lathe && fanuc => "U — Incremental em X, em diâmetro (torno). Nos ciclos G71/G73/G74/G75: parâmetro.",
            'W' when lathe && fanuc => "W — Incremental em Z (torno). Nos ciclos G71/G72: parâmetro.",
            'I' => "I — Centro do arco em X (incremental a partir do início, em raio). Também parâmetro de ciclos.",
            'J' => "J — Centro do arco em Y (incremental a partir do início).",
            'K' => fanuc
                ? "K — Centro do arco em Z; nos ciclos de furação, número de repetições."
                : "K — Centro do arco em Z; no G33, passo da rosca.",
            'R' => fanuc
                ? "R — Raio do arco (G02/G03); plano R nos ciclos de furação; parâmetro de ciclos do torno."
                : "R — Parâmetro de cálculo (R1, R2...).",
            'F' => "F — Avanço: mm/min (G94/G98) ou mm por rotação (G95/G99). Na rosca, o passo.",
            'S' => "S — Rotação do fuso (rpm) ou velocidade de corte (m/min) com G96.",
            'T' => lathe && fanuc ? "T — Ferramenta e corretor: T0101 = ferramenta 01, corretor 01 (troca na hora)." : "T — Seleciona a ferramenta (troca com M06).",
            'D' => "D — Corretor de raio da ferramenta." + NotDrawn,
            'H' => "H — Corretor de comprimento da ferramenta (G43/G44)." + NotDrawn,
            'P' => fanuc
                ? "P — Depende do código: subprograma (M98), tempo em ms (G04/G82), bloco inicial do perfil (G70–G73), parâmetro do G76."
                : "P — Número de repetições (L10 P3, REPEAT ... P=n).",
            'Q' => "Q — Bicada no G83; bloco final do perfil (G70–G73); parâmetros dos ciclos de torno (em µm sem ponto decimal).",
            'L' => fanuc ? "L — Número de repetições (M98, ciclos)." : "L — Chamada de subprograma: L10 chama o L10.SPF.",
            'O' => "O — Número do programa.",
            'E' => "E — Avanço de rosca em polegadas." + NotDrawn,
            'A' or 'B' or 'C' => $"{letter} — Eixo rotativo." + NotSupported,
            _ => null,
        };
    }

    // ───────────────────────── Palavras-chave ─────────────────────────

    private static readonly Dictionary<string, string> SiemensKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CYCLE81"] = "CYCLE81 — Furação (centrar).\nCYCLE81(RTP, RFP, SDIS, DP, DPR)\nRTP plano de retorno · RFP plano de referência · SDIS distância de segurança\nDP profundidade absoluta · DPR profundidade relativa ao RFP",
        ["CYCLE82"] = "CYCLE82 — Furação com espera no fundo.\nCYCLE82(RTP, RFP, SDIS, DP, DPR, DTB)\nDTB espera no fundo (s; negativo = rotações)",
        ["CYCLE83"] = "CYCLE83 — Furação profunda em bicadas.\nCYCLE83(RTP, RFP, SDIS, DP, DPR, FDEP, FDPR, DAM, DTB, DTS, FRF, VARI)\nFDEP/FDPR 1ª profundidade (absoluta/relativa) · DAM redução a cada bicada\nDTB espera no fundo · VARI 0 = quebra-cavaco, 1 = retorno total",
        ["CYCLE84"] = "CYCLE84 — Macho rígido." + NotSupported,
        ["CYCLE85"] = "CYCLE85 — Mandrilamento." + NotSupported,
        ["CYCLE86"] = "CYCLE86 — Mandrilamento com orientação do fuso." + NotSupported,
        ["CYCLE93"] = "CYCLE93 — Canal (torno)." + NotSupported,
        ["CYCLE95"] = "CYCLE95 — Desbaste/acabamento de contorno (torno).\nCYCLE95(NPP, MID, FALZ, FALX, FAL, FF1, FF2, FF3, VARI, DT, DAM, _VRT)\nNPP contorno: \"KONTUR\" (subprograma) ou \"INICIO:FIM\" (rótulos)\nMID profundidade · FALZ/FALX/FAL sobremetais · FF1 desbaste · FF3 acabamento\nVARI 1–4 desbaste, 5–8 acabamento, 9–12 completo (long./face, externo/interno)\n_VRT recuo",
        ["CYCLE97"] = "CYCLE97 — Rosca (torno).\nCYCLE97(PIT, MPIT, SPL, FPL, DM1, DM2, APP, ROP, TDEP, FAL, IANG, NSP, NRC, NID, VARI, NUMT, _VRT)\nPIT passo (ou MPIT = M3...M60) · SPL/FPL início/fim em Z · DM1/DM2 diâmetros\nAPP entrada · ROP saída · TDEP profundidade · FAL sobremetal · NRC passes · NID passes em vazio\nVARI 1/3 externa, 2/4 interna (1/2 profundidade constante, 3/4 seção constante)",
        ["CYCLE800"] = "CYCLE800 — Plano inclinado (4/5 eixos)." + NotSupported,
        ["POCKET3"] = "POCKET3 — Bolsão retangular." + NotSupported,
        ["POCKET4"] = "POCKET4 — Bolsão circular." + NotSupported,
        ["MCALL"] = "MCALL — Chamada modal: MCALL CYCLE81(...) executa o ciclo em cada posição seguinte; MCALL sozinho cancela.",
        ["TRANS"] = "TRANS — Desloca o zero (absoluto, substitui o anterior): TRANS X.. Y.. Z..",
        ["ATRANS"] = "ATRANS — Desloca o zero somando ao deslocamento atual: ATRANS X.. Y.. Z..",
        ["ROT"] = "ROT — Rotação de coordenadas." + NotSupported,
        ["AROT"] = "AROT — Rotação aditiva de coordenadas." + NotSupported,
        ["SCALE"] = "SCALE — Escala." + NotSupported,
        ["MIRROR"] = "MIRROR — Espelhamento." + NotSupported,
        ["DIAMON"] = "DIAMON — Torno: X em diâmetro (padrão).",
        ["DIAMOF"] = "DIAMOF — Torno: X em raio.",
        ["DIAM90"] = "DIAM90 — Torno: diâmetro em G90, raio em G91 (tratado como DIAMON).",
        ["LIMS"] = "LIMS= — Rotação máxima do fuso com G96 (rpm).",
        ["CR"] = "CR= — Raio do arco (G2/G3). Negativo = arco maior que 180°.",
        ["MSG"] = "MSG(\"texto\") — Mostra a mensagem no comando (aparece na simulação). MSG() apaga.",
        ["GOTOF"] = "GOTOF — Salta para a frente até o rótulo ou bloco N.",
        ["GOTOB"] = "GOTOB — Salta para trás até o rótulo ou bloco N.",
        ["GOTO"] = "GOTO — Salta para o rótulo (procura para a frente e depois para trás).",
        ["GOTOC"] = "GOTOC — Como GOTO, mas sem alarme se o destino não existir.",
        ["IF"] = "IF condição ... ELSE ... ENDIF — ou IF condição GOTOF rótulo.",
        ["ELSE"] = "ELSE — Ramo do IF quando a condição é falsa.",
        ["ELSEIF"] = "ELSEIF — Outra condição dentro do IF.",
        ["ENDIF"] = "ENDIF — Fim do IF.",
        ["WHILE"] = "WHILE condição ... ENDWHILE — Repete enquanto a condição for verdadeira.",
        ["ENDWHILE"] = "ENDWHILE — Fim do WHILE.",
        ["FOR"] = "FOR R1=0 TO 10 ... ENDFOR — Repete contando de um valor a outro.",
        ["ENDFOR"] = "ENDFOR — Fim do FOR.",
        ["REPEAT"] = "REPEAT ... UNTIL condição — repete até a condição ficar verdadeira.\nREPEAT INICIO FIM P=n — repete o trecho entre os rótulos n vezes.",
        ["REPEATB"] = "REPEATB ROTULO P=n — Repete a linha do rótulo n vezes.",
        ["UNTIL"] = "UNTIL condição — Fim do REPEAT.",
        ["LOOP"] = "LOOP ... ENDLOOP — Laço sem fim (sai com GOTO).",
        ["ENDLOOP"] = "ENDLOOP — Fim do LOOP.",
        ["DEF"] = "DEF — Declara variável: DEF REAL A=1, B / DEF INT N.",
        ["RET"] = "RET — Fim de subprograma (como M17).",
        ["IC"] = "IC(...) — Valor incremental só neste eixo: X=IC(5).",
        ["AC"] = "AC(...) — Valor absoluto só neste eixo: X=AC(5).",
        ["CFTCP"] = "CFTCP — Avanço constante no centro da ferramenta." + NotDrawn,
        ["CFC"] = "CFC — Avanço constante no contorno." + NotDrawn,
        ["SOFT"] = "SOFT — Aceleração suave." + NotDrawn,
        ["BRISK"] = "BRISK — Aceleração brusca." + NotDrawn,
        ["STOPRE"] = "STOPRE — Para a leitura antecipada de blocos." + NotDrawn,
    };

    private static readonly Dictionary<string, string> FanucKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IF"] = "IF [condição] GOTO n — salta para N n se verdadeira.\nIF [condição] THEN #1=... — executa a atribuição se verdadeira.\nComparação: EQ NE GT GE LT LE; lógica: AND OR XOR.",
        ["THEN"] = "THEN — Parte do IF [condição] THEN.",
        ["GOTO"] = "GOTO n — Salta para o bloco N n (o número pode ser expressão: GOTO #10).",
        ["WHILE"] = "WHILE [condição] DOn ... ENDn — Repete enquanto verdadeira (n = 1, 2 ou 3).",
        ["DO"] = "DOn — Início do laço WHILE (ou laço sem condição, se sozinho).",
        ["END"] = "ENDn — Fim do laço DOn.",
    };

    private static readonly Dictionary<string, string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SIN"] = "SIN — Seno (ângulo em graus).",
        ["COS"] = "COS — Cosseno (ângulo em graus).",
        ["TAN"] = "TAN — Tangente (ângulo em graus).",
        ["ATAN"] = "ATAN — Arco tangente em graus (Fanuc: ATAN[a]/[b]).",
        ["ATAN2"] = "ATAN2 — Arco tangente de a/b em graus.",
        ["ASIN"] = "ASIN — Arco seno em graus.",
        ["ACOS"] = "ACOS — Arco cosseno em graus.",
        ["SQRT"] = "SQRT — Raiz quadrada.",
        ["ABS"] = "ABS — Valor absoluto.",
        ["ROUND"] = "ROUND — Arredonda para o inteiro mais próximo.",
        ["FIX"] = "FIX — Arredonda para baixo (descarta as casas).",
        ["FUP"] = "FUP — Arredonda para cima.",
        ["TRUNC"] = "TRUNC — Descarta as casas decimais.",
        ["POT"] = "POT — Quadrado do valor.",
        ["MOD"] = "MOD — Resto da divisão.",
        ["LN"] = "LN — Logaritmo natural.",
        ["EXP"] = "EXP — Exponencial.",
    };

    // ───────────────────────── Variáveis ─────────────────────────

    private static string? FanucVariable(int number) => number switch
    {
        0 => "#0 — Vazio (sem valor).",
        >= 1 and <= 33 => $"#{number} — Variável local (cada chamada G65 tem as suas)." + (MacroLetter(number) is { } letter ? $"\nNo G65, recebe o argumento {letter}." : ""),
        >= 100 and <= 199 => $"#{number} — Variável comum (apagada ao desligar).",
        >= 500 and <= 999 => $"#{number} — Variável comum permanente (mantida ao desligar).",
        3000 => "#3000 — Alarme do usuário: #3000=1 (MENSAGEM) para o programa com alarme.",
        3901 => "#3901 — Peças usinadas (contador).",
        3902 => "#3902 — Peças pedidas.",
        >= 4001 and <= 4130 => $"#{number} — Estado modal (ex.: #4001 grupo de movimento, #4003 G90/G91, #4109 avanço, #4120 ferramenta).",
        5001 or 5002 or 5003 => $"#{number} — Posição final do bloco anterior em {"XYZ"[number - 5001]} (coordenadas da peça).",
        >= 5021 and <= 5023 => $"#{number} — Posição atual em {"XYZ"[number - 5021]} (coordenadas da máquina).",
        >= 5041 and <= 5043 => $"#{number} — Posição atual em {"XYZ"[number - 5041]} (coordenadas da peça).",
        _ => $"#{number} — Variável de sistema." + NotDrawn,
    };

    private static char? MacroLetter(int number) => number switch
    {
        1 => 'A', 2 => 'B', 3 => 'C', 4 => 'I', 5 => 'J', 6 => 'K', 7 => 'D', 8 => 'E', 9 => 'F',
        11 => 'H', 13 => 'M', 17 => 'Q', 18 => 'R', 19 => 'S', 20 => 'T', 21 => 'U', 22 => 'V',
        23 => 'W', 24 => 'X', 25 => 'Y', 26 => 'Z',
        _ => null,
    };

    [GeneratedRegex(@"#\d+|\bR\d+\b|[A-Z_][A-Z0-9_]*", RegexOptions.IgnoreCase)]
    private static partial Regex PieceRegex();

    // ───────────────────────── Consulta ─────────────────────────

    /// <summary>
    /// Explicação do que está na coluna (base 0) da linha. Null se não houver nada a explicar
    /// (espaço, comentário, número solto).
    /// </summary>
    public static string? Describe(string line, int column, Dialect dialect, MachineKind machine = MachineKind.Mill)
    {
        var tokens = new List<Token>();
        GCodeLexer.TokenizeLine(line, dialect, tokens);
        foreach (Token token in tokens)
        {
            if (column < token.Column || column >= token.Column + token.Length)
            {
                continue;
            }

            string text = line.Substring(token.Column, token.Length);
            return token.Kind switch
            {
                TokenKind.Word => DescribeWord(token.Letter, token.Value, dialect, machine),
                TokenKind.BlockNumber => Address('N', dialect, machine),
                TokenKind.Comment => null,
                TokenKind.Label => $"{text} — Rótulo (destino de GOTOF/GOTOB/REPEAT).",
                TokenKind.ProgramMarker => "Início/fim ou nome do programa.",
                _ => DescribePiece(text, column - token.Column, dialect, machine),
            };
        }

        return null;
    }

    /// <summary>Explicação de uma palavra de endereço (G01, M30, X10...).</summary>
    public static string? DescribeWord(char letter, double? value, Dialect dialect, MachineKind machine = MachineKind.Mill)
    {
        letter = char.ToUpperInvariant(letter);
        bool fanuc = dialect == Dialect.Fanuc;

        if (letter == 'G' && value is { } g)
        {
            if (g != Math.Floor(g))
            {
                return $"G{g.ToString(CultureInfo.InvariantCulture)} — Código G com decimal." + NotSupported;
            }

            int code = (int)g;
            if (fanuc && machine == MachineKind.Lathe && FanucLatheG.TryGetValue(code, out string? lathe))
            {
                return lathe;
            }

            if ((fanuc ? FanucG : SiemensG).TryGetValue(code, out string? specific))
            {
                return specific;
            }

            return CommonG.TryGetValue(code, out string? common) ? common : $"G{code} — Código G não reconhecido." + NotSupported;
        }

        if (letter == 'M' && value is { } m)
        {
            int code = (int)m;
            if (fanuc && FanucM.TryGetValue(code, out string? specific))
            {
                return specific;
            }

            return CommonM.TryGetValue(code, out string? common) ? common : $"M{code} — Função auxiliar da máquina." + NotDrawn;
        }

        return Address(letter, dialect, machine);
    }

    /// <summary>Palavra-chave, função, variável ou endereço dentro de um trecho (IF, CYCLE81(...), #5001, X=R1...).</summary>
    private static string? DescribePiece(string text, int offset, Dialect dialect, MachineKind machine)
    {
        // Pedaço exato sob o cursor: #123, R12 ou um identificador
        foreach (Match match in PieceRegex().Matches(text))
        {
            if (offset < match.Index || offset >= match.Index + match.Length)
            {
                continue;
            }

            string piece = match.Value.ToUpperInvariant();
            if (piece.StartsWith('#') && int.TryParse(piece[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                return FanucVariable(number);
            }

            if (dialect == Dialect.Siemens && piece.Length > 1 && piece[0] == 'R' && piece[1..].All(char.IsAsciiDigit))
            {
                return $"{piece} — Parâmetro de cálculo (variável do Siemens).";
            }

            if (Functions.TryGetValue(piece, out string? function))
            {
                return function;
            }

            string? keyword = dialect == Dialect.Siemens
                ? SiemensKeywords.GetValueOrDefault(piece)
                : FanucKeywords.GetValueOrDefault(SplitNumbered(piece));
            if (keyword is not null)
            {
                return keyword;
            }

            // Siemens "X=..." / Fanuc "X#7": endereço com expressão
            if (piece.Length == 1)
            {
                return Address(piece[0], dialect, machine);
            }

            return null;
        }

        return null;
    }

    /// <summary>"GOTO260" → "GOTO", "DO1" → "DO", "END1" → "END".</summary>
    private static string SplitNumbered(string piece)
    {
        foreach (string prefix in (string[])["GOTO", "DO", "END"])
        {
            if (piece.StartsWith(prefix, StringComparison.Ordinal) && piece.Length > prefix.Length && piece[prefix.Length..].All(char.IsAsciiDigit))
            {
                return prefix;
            }
        }

        return piece;
    }
}
