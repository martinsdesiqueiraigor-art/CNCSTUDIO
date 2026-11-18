## TORNO-01: relatório
**Feito:** plano do torno do CAM em `docs/tarefas/TORNO-01-plano.md` (itens a a j da ficha). Só documento, sem código.
Resumo (leitura de código, nada rodado):
- O torno do Core tem faceamento, torneamento externo e interno (desbaste G71/G70 ou CYCLE95), furo, rosca (G76 ou CYCLE97) e canal radial externo e interno. Canal de face e sangramento **não existem**.
- A tela já tem a casca da fresa, mas sem cartão Máquina, sem Programa nº e G5x digitados (1002 e 54 de fábrica, sem campo), sem escolha de Z0, e a operação nova nasce com valores padrão.
- Fixação é só informativa e tem 3 tipos (castanhas, placa, entre pontas); luneta não existe. 2ª fixação e Zero por trabalho estão "em breve".
- O Enviar ao Code do torno manda só o texto; o torno não usa o pacote `.casillas-cam`.
- Proposta: 8 fatias (T0 a T7). **Mínimo para o torno estar pronto: T0 ajustes de tela (P), T1 Preparação (M), T2 canal de face (G), T3 sangramento (G).** Depois: fixação, zero, 2ª fixação e pacote.
- Sangramento e canal de face são G-code novo e nunca rodaram em máquina; mexer no `.casillas-job` (2ª fixação) e no Z (face oposta) é G e decisão sua.
**Arquivos:** `docs/tarefas/TORNO-01-plano.md`, `docs/tarefas/TORNO-01-volta.md` (este).
**Testes:**
- Aprovados: nenhum rodado (só documentos).
- Reprovados: nenhum.
- Não executados: build e suíte (não há código; ramo `doc-*` não dispara a Prévia).
- Teste mestre (8 G-codes): não rodado (nada de código mudou).
- Testes alterados: nenhum.
**Limitações e pendências:**
- Não conferido: conteúdo dos testes do torno; causa da faixa rolando em 1280 e do botão 3D sobre fundo escuro; como o Code lê pacote de torno; faixa de Ø do bedame de face; barras MVQNR; o que é a "regra antiga do fuso".
- Fora do plano por ordem sua: banco de ferramentas, armário, operações novas de fresa, Modelo 3D.
- Duas fichas do torno estavam na pasta; segui `TORNO-01.md` (a `ficha-TORNO-01-plano-2026-11-14.md`, mais antiga, não foi usada).
**Decisões (detalhe e opções no plano, seção h; recomendação primeiro):**
1. Valores de fábrica: Programa nº, G5x e refrigeração digitados; operação nova como hoje (a) / tudo vazio como a fresa (b) / manter (c).
2. Bruto do torno: no trabalho, digitado (b) / só leitura (a) / obrigatório e some por operação (c).
3. Z0: campo por trabalho, desloca os Z (a) / só texto (b) / manter (c).
4. Bedame de face e sangramento: usar o bedame do banco (a) / tipo novo no banco (b).
5. Ø final do sangramento: digitado, 0 = até o centro (a) / sempre até o centro (b) / sempre com pino (c).
6. Sangramento e canal de face: avanço reduzido digitado e limite de rotação obrigatório (a) / avanço único (b).
7. Tipos de fixação: acrescentar castanha dura, castanha mole, contraponto e luneta (a) / manter (b).
8. Arquivo com 2 fixações: versão 2 (a) / versão 1 com campo opcional (b) / um arquivo por fixação (c).
9. Lado 2 a partir do lado 1: derivado e editável (a) / digitado com sugestão (b) / só digitado (c).
10. Pacote do torno: sim, depois do mínimo (a) / dentro do mínimo (b) / não (c).
11. Ajustes de tela T0: juntos em um PR (a) / um por ajuste (b).
**Como validar:**
1. Abrir este PR e conferir que só tem os 2 arquivos acima.
2. Ler o resumo acima e a seção (b) do plano (o que falta).
3. Responder as decisões 1 a 11; a lista de fatias (seção i) depende delas.
