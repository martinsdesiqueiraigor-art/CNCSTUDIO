# TORNO-T0: ajustes de tela do torno, Volta (14/11/2026)

```
ID: TORNO-T0 (ramo ajuste-TORNO-T0, base origin/dev 1cc92a6)
O que foi feito (até 5 linhas):
  1) Faixa do Trabalho torno cabe em 1280×720: botões de 58 px no mínimo (eram 66), só no torno. 2) Aviso "T5 não está no banco
  "X". O trabalho usa a cópia dele." na operação e na linha de avisos do torno, mesmo texto e estilo da fresa; trocar o banco ativo
  liga e desliga o aviso. 3) Botão 3D com cores do tema: cinza legível desligado, laranja da seleção ligado. Só visual.
Causa da rolagem em 1280 (medida no headless, Avalonia, janela 1204 = 1280 − trilho de 76 px):
  A faixa tem 15 botões de 66 px mínimo (dois mais largos pela palavra "Torneamento"/"Faceamento") e pedia 1093 px; a área
  útil da faixa é 1023 px (1204 − 16 de margem − 159 do "Banco ativo" fixo à direita − 6 de espaço − 1 de divisória, já com o
  banco do UX-04h D1). Sobravam 70 px, daí a barra de rolagem. Com mínimo de 58 px a faixa pede 997 px (folga de 26).
  Nenhum botão escondido, ordem dos grupos igual. A fresa não mudou (parâmetro novo da BuildPalette, padrão 66).
Arquivos alterados/criados:
  Core: Cam/ToolBankPick.cs (LatheConflict). App: Modules/CamShell.cs (minButtonWidth), Modules/LatheJobView.axaml (estilo do 3D),
  LatheJobView.axaml.cs (ShowWarnings, faixa), LatheJobView.Props.cs (aviso na operação, RefreshBank). Testes novos:
  ToolBankLatheConflictTests.cs (5). Docs: esta Volta.
Testes: dotnet test Core em Release, 1889 verdes (inclui CamJobGoldenTests, 8 G-codes, igual antes e depois); build sem aviso.
  Nada em pós, ciclos, Lathe.cs, .casillas-job ou banco foi tocado.
Conferido por foto headless 1280 (trilho de 76 px): faixa inteira até Rosca sem barra; 3D desligado e ligado legíveis.
Não conferido: o .exe com o torno-eixo.casillas-job aberto (item 4: nenhum código de geração mudou, os 8 G-codes seguem iguais);
  o aviso na tela com um banco real de torno (coberto só por teste do Core).
Decisões tomadas: (só as autorizadas)
  Largura 58 px só no torno (a ficha deixa estender à fresa só se ela couber igual; não mexi).
Riscos ou contradições encontradas:
  - No mesmo headless a faixa da fresa pede 1144 px para 1023 de área útil (rolaria). Não é escopo; fica o registro.
  - Fonte mais larga que a do headless pode comer a folga de 26 px; se rolar no seu .exe, diga.
```
