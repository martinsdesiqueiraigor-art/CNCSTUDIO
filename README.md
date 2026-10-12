# CncStudio

Editor e simulador de programas CNC (FANUC e Siemens), leve e rápido, no estilo do CIMCO Edit.

> **Status:** Etapa 1 — protótipo para validar a stack (C# / .NET 10 + Avalonia + AvaloniaEdit + SkiaSharp).
> Incremento 1 de 4 concluído: núcleo (Core), testes e arquivos de exemplo. A interface vem no incremento 2.

## Estrutura

```
CncStudio.sln
src/CncStudio.Core/          Lógica pura, sem UI e sem pacotes NuGet
  Dialects/                  Dialeto (Fanuc/Siemens) e detecção pela extensão
  Lexing/                    Lexer linha a linha (também usado pelo highlighting)
  Parsing/                   Parser com estado modal e diagnósticos em português
  Trajectory/                Trajetória da ferramenta (arcos interpolados)
  Simulation/                HeightMap 2.5D e simulador de remoção de material
tests/CncStudio.Core.Tests/  Testes xUnit do Core
samples/                     EXEMPLO.nc (Fanuc) e EXEMPLO.mpf (Siemens)
```

## Como compilar e testar (Windows 11)

1. Instale o **.NET 10 SDK**: https://dotnet.microsoft.com/download/dotnet/10.0
2. Clone o repositório e rode os testes:

```powershell
git clone https://github.com/martinsdesiqueiraigor-art/cncstudio.git
cd cncstudio
dotnet test
```

O resultado esperado é todos os testes aprovados.

## Escopo do protótipo

- **Comandos:** G00, G01, G02, G03 (centro por I/J), M03/M04/M05, M30/M02, F, S.
- **Condições assumidas:** milímetros, plano XY, coordenadas absolutas, sem compensação de raio.
- **Aceitos e ignorados sem aviso:** G17, G21, G40, G49, G54–G59, G64, G80, G90, G94, T, D, H, M06–M09
  (Siemens: também G60, G71, G500).
- **Geram aviso:** outros G/M, ciclos (CYCLE81...), CR=, R.
- **Geram erro:** G91, G20, G18/G19, arco sem I/J, texto não reconhecido.
- Números sem ponto decimal (ex.: `X10`) são lidos como milímetros.

## Licença

A definir (MIT provável).
