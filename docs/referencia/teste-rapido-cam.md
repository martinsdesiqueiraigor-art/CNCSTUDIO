# Teste rápido do CAM (desktop)

**Status: rascunho.** Só o Igor marca como revisado, depois de fazer o roteiro do começo ao fim. Tempo-alvo: **2 minutos**.
Sempre o mesmo, a cada PR. Se um passo não bater, anote o número do passo e o que apareceu.

## Antes
- Abra o `.exe` da prévia com o atalho **Testar PR** (`tools/Testar PR.cmd`) ou pelo link no comentário do PR. O atalho roda de qualquer pasta (o repositório vem fixo no script) e não depende do ramo em que a pasta principal está.
- Peça fixa: **`fresa-sem-bruto.casillas-job`**, em `tests/CncStudio.Core.Tests/Cam/trabalhos-referencia/` do repositório (o G-code esperado está ao lado, em `fresa-sem-bruto.nc`).
  - Por quê: é a fresa mais simples dos 8 trabalhos do teste mestre (`CamJobGoldenTests`), sem STEP, banco de ferramentas ou zero especial.
  - Observação: a pasta `samples/` não tem nenhum arquivo que o CAM abra (só `.nc` e `.mpf`, que são do Code). Por isso o caminho aponta para `tests/`. Decisão do Igor: copiar um trabalho para `samples/` seria outra ficha.

## Passos
| # | O que fazer | Onde (arquivo da tela) | O que deve aparecer |
|---|---|---|---|
| 1 | Abrir o CAM | A definir: o caminho do aplicativo até o módulo CAM (a tela do módulo é `src/CncStudio.App/Modules/CamView.axaml`; abas **Fresa**, **Torno**, **Ferramentas**, **Mais ▾**) | Aba **Fresa** ativa, com a árvore à esquerda e o aviso "Abra um DXF (faixa Arquivo), um trabalho salvo ou desenhe uma forma…" no centro (`JobView.axaml`) |
| 2 | Carregar a peça | Faixa **Arquivo** → botão **Abrir** (`Core/Cam/CamPalette.cs`), diálogo "Abrir trabalho do CAM" (`MainWindow.Modules.cs`); escolha `fresa-sem-bruto.casillas-job` | A árvore e o desenho da peça preenchidos; nome do trabalho no campo **Trabalho:** |
| 3 | Gerar o programa | Botão **Gerar G-code** na barra de baixo (`Modules/JobBar.cs`) | Chips **Trajetória ✓** e **G-code ✓**. Se aparecer "Não dá para gerar", anote os itens. Comparar o G-code com `fresa-sem-bruto.nc`: A definir (o app não mostra a comparação) |
| 4 | Simular | Botão **Enviar ao Code para simular** (`Modules/JobBar.cs`) | O Code abre com o programa. Rodar a simulação lá e ver a trajetória sem erro. O texto exato do que o Code mostra ao terminar: A definir. No CAM, o chip fica **Simulado: não verificado** (é o esperado) |

## Anotações do Igor
- Passo que não bateu:
- O que apareceu:
