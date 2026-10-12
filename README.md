# CncStudio

Editor e simulador de programas CNC (FANUC e Siemens), leve e rápido, no estilo do CIMCO Edit.

> **Status:** Etapa 2 — produto final (C# / .NET 10 + Avalonia + AvaloniaEdit).
> Fases 1 (comandos de fresa) e 1B (interpretador de macros) concluídas. Plano completo em [docs/PLANO-ETAPA2.md](docs/PLANO-ETAPA2.md).

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
- **Backplot:** vistas XY, XZ e YZ; G00 tracejado, corte contínuo; a linha do cursor fica em destaque;
  roda do mouse = zoom, arrastar = mover, duplo clique = ajustar.
- **Simulação 2.5D:** vistas superior (cor = profundidade), frontal e lateral (perfil com corte na
  posição da ferramenta) e isométrica; executar/pausar, passo por linha, até o fim, reiniciar,
  velocidade e diâmetro da ferramenta (fresa de topo reto).

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

## Licença

A definir (MIT provável).
