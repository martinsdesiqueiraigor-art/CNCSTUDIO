# CncStudio

Editor e simulador de programas CNC (FANUC e Siemens), leve e rápido, no estilo do CIMCO Edit.

> **Status:** Etapa 2 — produto final (C# / .NET 10 + Avalonia + AvaloniaEdit).
> Fases 1 (comandos de fresa), 1B (interpretador de macros), 2 (estatísticas), 3 (torno) 4 (backplot passe a passe), 5 (interface da simulação) e 6 (editor e funções NC) concluídas. Plano completo em [docs/PLANO-ETAPA2.md](docs/PLANO-ETAPA2.md).

## Testar sem instalar nada (executável pronto)

A cada commit, o GitHub Actions compila, roda os testes e gera o executável para Windows:

1. No GitHub, abra a aba **Actions** → clique na execução mais recente com ✅.
2. Em **Artifacts**, baixe **CncStudio-win-x64** e extraia o .zip.
3. Rode **CncStudio.exe**. Se o Windows SmartScreen avisar (o executável não é assinado),
   clique em **Mais informações → Executar assim mesmo**.

O programa abre já com o `EXEMPLO.nc` carregado.

## Compilar e rodar a partir do código (Windows 11)

1. Instale o **.NET 10 SDK**: https://dotnet.microsoft.com/download/dotnet/10.0
2. Na pasta do projeto:

```powershell
dotnet test                                # roda os testes do Core
dotnet run --project src/CncStudio.App     # abre o programa
```

Para testar desempenho com um arquivo grande (10 MB):

```powershell
.\tools\gerar-arquivo-grande.ps1           # cria GRANDE.nc na pasta atual
```

## O que o protótipo faz

- **Editor:** highlighting Fanuc/Siemens, números de linha, Ctrl+O / Ctrl+S, dialeto pela extensão
  (com troca manual), análise automática 500 ms após digitar, painel de avisos/erros clicável.
- **Backplot:** vistas XY, XZ, YZ, isométrica e Torno (ZX); cores G00 vermelho (tracejado), G01 verde,
  G02 azul-claro, G03 azul-escuro; a linha do cursor fica em destaque; roda do mouse = zoom,
  arrastar = mover, duplo clique = ajustar.
- **Passe a passe:** barra embaixo do backplot (◀ Passe / Passe ▶, executar, régua) mostra o caminho de
  cada passe, a posição da ferramenta, e o editor acompanha a linha.
- **Simulação 2.5D (fresa):** vistas superior (cor = profundidade), frontal e lateral (perfil com corte na
  posição da ferramenta) e isométrica; trajetória desenhada por cima do material; ferramenta com suporte;
  barra de reprodução com régua (arraste para qualquer ponto), passo por linha, ◀ Passe / Passe ▶,
  velocidade; painel com T/Ø, X, Y, Z, G-code e avanço, linha, passe e distância; o editor acompanha a linha.

- **Busca e funções NC:** procurar/substituir em português (maiúsculas, palavra inteira, expressão regular),
  remover/inserir espaços, remover comentários e números de bloco, renumerar, maiúsculas.
- **Ajuda no editor:** pare o mouse sobre G71, CYCLE83, #3000, GOTOF... para ver o que é e os parâmetros.
- **Clique na trajetória** (backplot ou simulação) para ir à linha; **📏 Medir** no backplot (encaixa nos pontos).
- **Limites de curso** (Máquina → Limites de curso): avisa a linha em que o programa passa do curso.
- **Painel de avisos/erros** com filtro e contagem.
- **Bruto** (Máquina → Bruto…, ou na Simulação): automático, informado (bloco X/Y/Z na fresa, Ø × comprimento
  no torno) ou lido do cabeçalho "(BRUTO 100 X 80 X 20)"; guardado por arquivo, desenhado no backplot, usado na
  simulação (que avisa G00 removendo material) e na folha de setup.
- **Folha de setup** (Arquivo → Relatórios): informações do programa atual (arquivo, número, cabeçalho,
  comando, máquina, zeros, limites), lista de ferramentas com descrição, tempo por ferramenta e total, e o
  desenho da trajetória. Abre no navegador: Ctrl+P → Salvar como PDF.

## Estrutura

```
CncStudio.sln
src/CncStudio.Core/          Lógica pura, sem UI e sem pacotes NuGet
  Dialects/                  Dialeto (Fanuc/Siemens) e detecção pela extensão
  Lexing/                    Lexer linha a linha (também usado pelo highlighting)
  Parsing/                   Parser com estado modal e diagnósticos em português
  Trajectory/                Movimentos (arcos guardados inteiros) e polilinhas para desenho
  Simulation/                HeightMap 2.5D, simulador de remoção e geração de imagens
src/CncStudio.App/           Interface Avalonia (editor, backplot, simulação)
tests/CncStudio.Core.Tests/  Testes xUnit do Core
samples/                     EXEMPLO.nc (Fanuc) e EXEMPLO.mpf (Siemens)
tools/                       Script para gerar arquivo grande de teste
```

## G-code suportado (Fase 1)

- **Movimento:** G00, G01, G02, G03 nos planos G17/G18/G19, por centro (I/J/K) ou raio (R no Fanuc, CR= no Siemens).
- **Modos:** G90/G91, G20/G21 (G70/G71 no Siemens), G54–G59 (G500 no Siemens), TRANS/ATRANS.
- **Furação:** G81, G82, G83 com G98/G99, Q e repetição K; CYCLE81, CYCLE82, CYCLE83, inclusive com MCALL.
- **Ferramentas:** T + M06; diâmetro por ferramenta em *Máquina → Ferramentas e zeros de peça*.
- **Sem desenho de movimento:** G04 (espera), G53 (coordenada de máquina), G28/G30.
- Números sem ponto no Fanuc (`X10`): 10 mm por padrão; configurável para 0,010 mm.

## Programação paramétrica (Fase 1B)

- **Fanuc (Macro B):** variáveis `#1`–`#999`, `#[...]`, contas e funções (SIN, COS, TAN, ATAN, SQRT,
  ABS, ROUND, FIX, FUP...), `IF [...] GOTO`, `IF [...] THEN`, `WHILE [...] DOn` / `ENDn`, `GOTO`,
  `M98 P.. L..`, `M99` (com `P`), `G65 P.. A.. B..` (argumentos em #1–#26), alarme `#3000`.
- **Siemens:** parâmetros `R`, `DEF`, `IF/ELSEIF/ELSE/ENDIF`, `WHILE`, `FOR`, `REPEAT/UNTIL`, `LOOP`,
  `GOTOF/GOTOB/GOTO`, rótulos, `REPEAT rótulo [rótulo] P=n`, `L10 P2`, chamada por nome, `IC()`/`AC()`, `MSG`.
- **Subprogramas:** no mesmo arquivo (O-number / `%_N_..._SPF`) ou na pasta do programa aberto
  (`O1234.nc`, `L10.SPF`, `FURO.SPF`...).
- **Cantos e compensação:** `,R` / `,C` (Fanuc) e G41/G42 com o diâmetro da tabela de ferramentas.
- **Proteção:** laço infinito e chamadas aninhadas demais interrompem a simulação com erro na linha.
- Exemplos em `samples/macros/`: ESPIRAL_WHILE.nc, SUBPROGRAMA.nc, CHAVETA.mpf, CHAMA_FURO.mpf + FURO.SPF.

## Estatísticas (Fase 2)

- Aba **Estatísticas**: tempo total estimado, tempo de corte, de rápido e de espera, distâncias,
  movimentos e trocas de ferramenta; tabela por ferramenta (clique para ir à 1ª linha) e limites X/Y/Z.
- Tempo de corte = comprimento ÷ avanço (arcos pelo comprimento real); rápido pela velocidade de
  *Máquina → Ferramentas e zeros de peça → Programa* (padrão 10.000 mm/min). Sem aceleração.
- Esperas: `G04 X`/`G04 P` (Fanuc), `G4 F`/`G4 S` (Siemens), `G82 P`, DTB do CYCLE82/CYCLE83.

## Torno (Fase 3)

- **Tipo de máquina:** *Automático* (detecta pelo programa: G71/G70/G76, U/W, G96, CYCLE95/97, DIAMON...), *Fresa* ou *Torno*,
  na caixa **Máquina** ao lado do dialeto. Programas de torno abrem o backplot na vista **Torno (ZX)**.
- **Coordenadas:** X em diâmetro (DIAMON/DIAMOF no Siemens), U/W incrementais (Fanuc), plano G18, ferramenta T0101.
- **Avanço e rotação:** G99/G98 (Fanuc) e G95/G94 (Siemens), G96/G97, limite G50 S / LIMS= / G26 S
  (padrão em *Máquina → Programa*). O avanço por rotação vira mm/min com a rotação no diâmetro do movimento.
- **Fanuc:** G71/G72 (desbaste, inclusive com rebaixo) + G70 (acabamento), G74/G75, G76 (rosca em passes), G90/G92/G94, G32.
- **Siemens:** CYCLE95 (contorno por subprograma `"KONTUR"` ou rótulos `"INICIO:FIM"`; VARI 1–12), CYCLE97, G33.
- Torno usa só o backplot (a simulação 2.5D é de fresa).
- **G71/G72 com rebaixo** (tipo II) e **G74/G75** (bicadas na face e canais em X).
- Exemplos: `samples/torno/EXEMPLO_TORNO.nc` e `EXEMPLO_TORNO.mpf` (botões *Torno Fanuc* / *Torno Siemens*).

## Licença

A definir (MIT provável).
