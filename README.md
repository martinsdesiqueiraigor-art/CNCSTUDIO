# CncStudio

Editor e simulador de programas CNC (FANUC e Siemens), leve e rápido, no estilo do CIMCO Edit.

> **Status:** Etapa 1 — protótipo para validar a stack (C# / .NET 10 + Avalonia, que desenha via Skia, + AvaloniaEdit).
> Os 4 incrementos do protótipo estão prontos e aguardam validação.

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

## Escopo de G-code do protótipo

- **Comandos:** G00, G01, G02, G03 (centro por I/J), M03/M04/M05, M30/M02, F, S.
- **Condições assumidas:** milímetros, plano XY, coordenadas absolutas, sem compensação de raio.
- **Aceitos e ignorados sem aviso:** G17, G21, G40, G49, G54–G59, G64, G80, G90, G94, T, D, H, M06–M09
  (Siemens: também G60, G71, G500).
- **Geram aviso:** outros G/M, ciclos (CYCLE81...), CR=, R.
- **Geram erro:** G91, G20, G18/G19, arco sem I/J, texto não reconhecido.
- Números sem ponto decimal (ex.: `X10`) são lidos como milímetros.

## Licença

A definir (MIT provável).
