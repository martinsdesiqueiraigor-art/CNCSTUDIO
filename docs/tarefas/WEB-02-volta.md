## WEB-02: relatório (Code web: bruto da fresa igual ao programa)

Ramo `ajuste-WEB-02`, base `dev` em `0e3037a`. Executor: Claude (chat), em sprint, por ordem do Igor (18/11). Pedido: "na fresa, seguir o princípio de bruto igual ao programa" → o comentário manda, ler redondo e tubo, posição igual à do programa. As ferramentas do torno ficaram como estão (o Igor conferiu: ok).

**Commits:** S2/S3 no Core (bruto do comentário na forma e na posição do programa) · S1 na web (o programa manda, "Outro bruto" de propósito).

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, 1982 (20 novos em `Web02ProgramStockTests`). Build Release do App sem erro; web publicada como no CI.
- Nenhum teste existente mudou. Os 8 G-codes do CAM não foram tocados.
- Conferido a olho num Chrome sem tela: fresa com `(BRUTO 100 X 80 X 20)` e setup antigo salvo com o canto no zero → simula centrado no zero, como o programa; `(BRUTO D50 X 30)` com `ZERO ... CENTRO` → cilindro em pé centrado; tela do setup nos dois modos (360×760). A conta foi contornada só na cópia local.

### S1 O comentário manda (só na web)
- Programa com comentário de bruto: a simulação, o 2D e a folha usam o bruto do programa, sem abrir nem salvar o setup. O setup salvo não é alterado; as ferramentas e a fixação dele continuam valendo.
- Setup → Bruto → "Origem do bruto": **Do programa (BRUTO …)** (campos travados, com as medidas do programa) ou **Outro bruto** (campos livres; grava a marca `OverridesProgram` e a simulação mostra aviso fixo no rodapé e na lista de G00).
- Setup antigo com bruto e sem a marca: passa a seguir o comentário (era a causa provável dos "10 rápidos no material").
- Vale também no torno (mesma regra). Barra quadrada ou sextavada no torno precisa de "Outro bruto", porque o comentário só descreve barra redonda.
- Desktop: sem mudança (lá o setup salvo continua mandando).

### S2 Redondo e tubo no comentário da fresa
- `(BRUTO D50 X 30)`, `(BRUTO REDONDO Ø50 X 30)`, `(STOCK DIAM 50 X 30)` → cilindro em pé (eixo Z).
- `(BRUTO D50 D30 X 30)`, `(BRUTO TUBO D50 X D30 X 30)`, `(BRUTO Ø50/Ø30 X 30)` → tubo.
- Bloco `(BRUTO 100 X 80 X 20)` como antes.

### S3 Posição igual ao programa
- A linha do CAM `(ZERO G54: CENTRO DA PECA, Z0 NO TOPO)` (e os outros 8 pontos, Z0 no topo ou na base) manda.
- Sem ela, por eixo, pelo corte: dos dois lados do zero → centrado no zero; só positivo → canto no zero; só negativo → canto oposto no zero; corte todo acima de Z0 → Z0 na base.
- Antes: centrado no meio do corte (ou canto no zero) e sempre Z0 no topo.
- O ponto do zero do CAM é da peça pronta; aqui ele é aplicado ao bruto. No centro dá igual; num canto, o bruto com sobra fica deslocado pela sobra (aviso para o Igor conferir).

### Teste do Igor (prévia `https://ajuste-web-02.casillas-code.pages.dev`, celular)
1. Fresa com `(BRUTO 100 X 80 X 20)` e cortes dos dois lados do zero: simular sem abrir o setup → bruto centrado.
2. Fresa com `(BRUTO D50 X 30)` → cilindro; com `(BRUTO D50 D30 X 30)` → tubo.
3. Programa com zero no canto (cortes só em X e Y positivos) → canto do bruto no zero.
4. Setup → "Outro bruto" → mudar uma medida → salvar → aviso fixo; voltar a "Do programa" → aviso some.

### Não conferido
- Aparelho real (só Chrome sem tela). Programa Siemens com comentário `;BRUTO` (o leitor é o mesmo da Fanuc).

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01B7m9Dz8ujacpQtkJpoFA8D
