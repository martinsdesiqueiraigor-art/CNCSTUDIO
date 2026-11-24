# CAM-ACHADOS-01 — Volta (três achados do teste completo do BOLTED_FLANGE)

## O que foi feito
1. **Gouge falso nos furos Ø9.** Duas causas, as duas corrigidas:
   - O bruto por operação (`CamStockTimeline`) simulava cada T com o Ø lido do comentário da troca. A broca "broca 9 mm" não tem "D9" no nome e virava a fresa padrão Ø10: o anel de 0,5 mm na parede do furo aparecia como gouge. Agora o bruto por operação usa o Ø da operação, como o pacote `.casillas-cam` leva ao Code (`CamHandoffCode.ApplyDiameters`, uma linha). Vale também para o rápido no material (CAM-RAPIDO-01).
   - A malha facetada do STEP fecha um pouco o furo (cordas dentro do círculo). Na peça × bruto (`CamStockCompare`), célula sem peça passa a contar como o fundo na vizinhança 3 × 3: a tolerância lateral de uma célula vale na parede do furo e na borda da silhueta, como já valia nas outras paredes.
2. **Motivo do "começar no material não aplicado".** Material acima do topo digitado (o caso do teste: material em Z1, topo digitado Z0) agora diz o motivo: "material em Z1 nesta área, acima do topo digitado Z0; o topo nunca sobe acima do digitado e o 1º passe tira também o excesso". Vai ao console como Conferir e ao selo e à aba Alturas. Core: `CamStockStart.AboveReason` (acréscimo). Material no topo digitado continua sem aviso; a tela diz "O material já está no topo digitado nesta área: nada a ajustar." em cinza, não amarelo.
3. **"Furação criada" como Erro.** "criada(s)" entrou na lista de Info do console (`AvisosConsole.Classify`), como "Operação criada".

Nada muda no G-code, no arquivo nem nos motores de simulação.

## Testes
- Novo `CamAchados01Tests` (4 métodos, 5 casos): furo Ø9 com broca Ø9 sem gouge (malha com 16 e 48 lados; antes: 165 e 152 mm³ de gouge); broca Ø10 no furo Ø9 ainda dá gouge (116 mm³); material acima do topo digitado diz o motivo, gera igual ao digitado, e a tela recebe o mesmo texto.
- **Testes existentes alterados (autorizados na ficha, ordem do Igor de 23/11):**
  - `CamStockCompareTests.Fora_da_peca_e_do_material_nao_entra_na_conta`: gouge esperado 10 × 10 × 6 → 8 × 8 × 6 (a borda de uma célula é tolerância lateral).
  - `CamStockCompareTests.Contorno_por_fora_que_entra_na_flange_de_100_da_gouge_na_borda`: teto do volume com uma célula de folga (raio 43,5 em vez de 44; deu 10 679 mm³ contra o teto antigo de 10 631) e comentário corrigido (a simulação usa o Ø20 da operação, não o Ø10 padrão).
- Core: **2196 verdes**, 8 G-codes intactos. A solução compila sem avisos.

## O que o Igor confere no .exe
1. `BOLTED_FLANGE-teste-completo`: a Furação dos 6 × Ø9 não mostra gouge nos furos.
2. Operação em "Topo a partir de: Material" com o material acima do topo digitado: o selo e a aba Alturas dizem o motivo.
3. Furação reconhecida → Criar operações: a linha "operação(ões) de Furação criada(s)" aparece como Info.

## Limites
- A borda de uma célula ao lado de célula sem peça deixou de acusar gouge (um corte que entra só uma célula na parede de fora não aparece). É o mesmo limite já escrito para as paredes.
- Sem teste no .exe (ordem do Igor).
