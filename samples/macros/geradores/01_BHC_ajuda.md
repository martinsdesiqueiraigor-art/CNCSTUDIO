# Gerador 01 — Furação em círculo (BHC)

**Estado:** rascunho técnico. Gera posições de 2 a 36 furos equidistantes num círculo, a partir do ângulo inicial medido de +X no sentido anti-horário. A macro `O9010` usa `G81` sem `Q` e `G83` quando `Q` é informado.

| Campo (G65) | Unidade | Exemplo |
|---|---|---|
| Centro X (`X`) | mm | 50 |
| Centro Y (`Y`) | mm | 40 |
| Diâmetro (`D`) | mm | 40 |
| Número de furos (`H`) | inteiro, 2 a 36 | 6 |
| Ângulo inicial (`A`) | graus | 0 (padrão) |
| Fundo (`Z`) | mm | -12 |
| Retorno (`R`) | mm | 2 (padrão) |
| Picada (`Q`) | mm | 3 (opcional) |
| Avanço (`F`) | mm/min | 90 |

**Exemplo:** `G65 P9010 X50. Y40. D40. H6 A0. Z-12. R2. Q3. F90.`

**Três erros comuns:** (1) informar raio em `D` quando a macro espera diâmetro; (2) inverter o sinal do fundo `Z`; (3) escolher centro/diâmetro que posicionam furos fora do bruto ou da fixação.

**Conferência antes da máquina:** confirmar G54, ferramenta, corretor de comprimento, origem, fixação, posições X/Y e profundidade. Simular todos os furos e executar teste controlado com afastamento adequado, sob responsabilidade de profissional habilitado. A macro não garante ausência de colisões.

**Atenção:** no `G65` o número de furos vai na letra `H` (a letra `N` não é argumento válido no Fanuc). Centro X/Y omitido vale 0. Validada no interpretador do Casillas Code (6 furos, posições conferidas); falta validar em controle real.

**A definir:** compatibilidade exata do interpretador com `G65`, `FIX`, `#3000` e ciclos fixos `G81/G83`; política de retorno `G98/G99`; instalação da macro protegida `O9010` no controle. A ferramenta e os parâmetros de corte devem ser ajustados à máquina.
