# CAM-AR-01a — Volta (começar no material: motor)

## O que foi feito
- Operação: campo novo `TopFromStock` ("Topo a partir de: Material"). Falso = topo digitado, como sempre.
- Core novo: `CamStockStart`. Mede o maior Z do material que sobrou na área varrida pelos movimentos de corte e pelos rápidos abaixo da altura segura (alargada pelo raio da ferramenta; duas vezes o raio com compensação no comando), no bruto depois da última operação com estado.
  - Topo efetivo = menor entre o digitado e o material; nunca sobe.
  - Altura de avanço = topo efetivo + o maior entre a folga (1 mm + célula do bruto) e a distância digitada entre avanço e topo; nunca acima da digitada.
  - Passes em Z contados do topo efetivo até o fundo. Fundo e altura segura não mudam.
  - Material no topo digitado ou acima: nada muda (G-code igual, sem aviso).
- Gerar em sequência: com alguma operação ligada em "Material", o `CamJobBuilder.Build` usa a `CamStockTimeline` (gera e simula uma operação de cada vez, a seguinte sobre o bruto da anterior já ajustada). Sem nenhuma, o `Build` segue o caminho de sempre. O bruto por operação da tela passa a mostrar a trajetória ajustada.
- `Build(job, machine, bank)`: parâmetro novo opcional do banco da fresa (forma das ferramentas).
- Arquivo: chave `topFromStock` na operação, **versão 9 só quando alguma operação usa**. Sem ela, o texto do arquivo é o de antes.
- Aviso Conferir por operação: "Operação N parte em Z… pelo bruto da operação M (altura de avanço…, passes em Z a → b). O G-code muda: confira na simulação."

## Mudança em relação à ficha (avisada no plano, OK do Igor)
- **Chanfro saiu**: ele conta o fundo a partir do topo (`TopZ − profundidade`); mudar o topo moveria o chanfro. Entram Faceamento, Contorno e Bolsão.
- **Forma de ferramenta desconhecida não ajusta**: sem o tipo no banco, o simulado usaria topo reto, que tira material demais com fresa esférica ou toroidal (bruto simulado abaixo do real: a ferramenta desceria em rápido no material). Vale para a ferramenta da operação e de todas as anteriores.

## Comportamento de referência copiado
Fusion, aba Heights ("Top Height from Stock top" + "Feed Height" com deslocamento), com o bruto em processo (Fusion "rest machining"; Mastercam "stock awareness").

## Casos em que gera pelo topo digitado (com aviso Conferir e o motivo)
Tipo que não usa (Chanfro, Furação, Texto); bruto em voxel (eixo rotativo, 3+2, cilindro deitado); ferramenta sem tipo no banco; sem material na área; sem bruto por operação (bruto ausente, incompleto, STEP, zero incompleto); falha ao gerar com as alturas novas.

## Testes
- Novos: `CamStockStartTests` (10): ajuste e passes; nunca acima do digitado; bolsão depois do faceamento (passes Z−5 e Z−8, nenhum corte acima do material, aviso); sem banco = G-code igual ao digitado + aviso; sem material; material no topo = nada muda; tipos que não usam; voxel (3+2) = G-code igual + aviso; programa inteiro simulado = bruto em sequência (0 células diferentes); arquivo v9 só com a opção, ida e volta.
- **Testes existentes alterados (autorizados na ficha, Igor 21/11):** só a "versão futura" de 9 para 10, porque a 9 passou a existir: `CamJobTests` (Versao_mais_nova…), `CamJobProgramTests` (Leitor_ate_v2…), `CamJobZeroTests` (Leitor_recusa_versao_maior…), `CamJobStockShapeTests` (Leitor_recusa_versao_maior…, também a mensagem "versão mais nova (10)").
- Core: **2156 verdes**, 8 G-codes (`CamJobGoldenTests`) intactos. App compila sem avisos.
- Tempo: o trabalho de teste (2 operações, bloco 70 × 50) gera em sequência em menos de 0,5 s; na peça grande cresce com o número de operações (o mesmo cálculo do bruto por operação, ≈2 s para 20 operações). Medir no BOLTED_FLANGE no .exe.

## O que ainda não está (fatia b)
- Nenhuma tela: a opção não aparece no app e o App ainda não passa o banco ao Gerar (sem banco, nada ajusta). Por isso **não há o que testar no .exe nesta fatia**.
- Fatia b: aba Alturas ("Topo a partir de"), selo "parte em Z…", chave "Operações novas: começar no material", aviso no console como Conferir, App passando o banco ao Gerar.

## Riscos
- Simulação é verificação, nunca garantia: se o bruto da máquina for maior que o simulado, a folga pode não bastar.
- A área inclui os movimentos de corte e os rápidos abaixo da altura segura (que passam a andar logo acima do material). Rápido na altura segura não entra.
