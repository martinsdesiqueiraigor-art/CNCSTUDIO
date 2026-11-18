## WEB-04: relatório (comentário BRUTO igual no torno e na fresa)

Ramo `ajuste-WEB-04`, base `dev` em `c3c224a`. Executor: Claude (chat), em sprint, por ordem do Igor (18/11, "De acordo").

**Commits:** Core (leitura das quatro formas, tubo furado no torno, sextavado na fresa) · web (sextavado no setup da fresa e texto do bruto).

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, 1996 (14 novos em `Web04ProgramStockTests`). Build Release da web sem erro.
- Nenhum teste existente mudou. Os 8 G-codes do CAM não foram tocados.
- Conferido a olho num Chrome sem tela: fresa `(BRUTO S60 X 30)` → sextavado em pé com o rasgo; torno `(BRUTO D100 X D70 X 100)` → tubo com furo; torno `(BRUTO S60 X 100)` → barra sextavada torneada; setup da fresa com o sextavado do programa (360×760).

### A escrita (a mesma nas duas máquinas; o último número é o comprimento no torno e a altura na fresa)
| Comentário | Torno | Fresa |
|---|---|---|
| `(BRUTO D100 X 60)` | barra Ø100 × 60 | cilindro em pé |
| `(BRUTO D100 X D70 X 100)` | tubo (também `D100 D70 X 100`, `Ø100/Ø70 X 100`) | tubo em pé |
| `(BRUTO 100 X 80 X 20)` | barra retangular 100 × 80, comprimento 20 | prisma |
| `(BRUTO S60 X 100)` | barra sextavada, chave 60 (também `SEXT60`, `SEXTAVADO 60`) | sextavado em pé |

- BRUTO, STOCK ou BLANK; maiúscula ou minúscula; X, × ou \*. "S" só vale colado a um número logo depois de BRUTO (`SAE` não vira sextavado).
- Torno: face em Z0. Fresa: posição pelo zero do programa, como no WEB-02 (o sextavado tem os cantos em X e as faces paralelas a X).

### O que mais mudou
- **Tubo no torno começa furado** (antes a simulação o tratava como barra maciça, também quando vinha do setup).
- **Sextavado na fresa:** a simulação só tem material dentro do sextavado; o setup da fresa ganhou "Sextavado (em pé)" (chave e altura).
- Desktop: a leitura do comentário continua a antiga (bloco na fresa, barra no torno); o tubo furado no torno vale também no desktop quando o setup tem tubo.

### Teste do Igor (prévia `https://ajuste-web-04.casillas-code.pages.dev`)
1. Torno: os quatro comentários da tabela, simulando sem abrir o setup.
2. Fresa: os quatro, com o zero no centro.
3. Setup → Bruto mostra "Do programa (BRUTO …)" com a forma certa.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01B7m9Dz8ujacpQtkJpoFA8D
