# UI-PRO-P2: console de avisos
Nível M · Base `dev` em `d3123f9` (a ficha dizia "depois da OPS-BC-01", adiada por ordem do Igor em 19/11) · Ramo `ajuste-UI-PRO-P2`

## O que mudou (só `src/CncStudio.App`)
- **Console novo (`AvisosConsole.cs`)** sob o 3D, na fresa e no torno, no lugar das linhas laranja. Uma linha por mensagem, com etiqueta de nível: Erro, Aviso, Conferir ou Info, do mais grave ao menos grave. Cabeçalho clicável recolhe e abre; recolhido (ou aberto), mostra a contagem por nível ("Avisos (3) · 1 erro · 1 aviso · 1 conferir"); sem nada: "Avisos · nenhum".
- **Texto vermelho da coluna da árvore** (`Errors`) também foi para o console (decisão A do Igor, 19/11). A escrita de antes (`Errors.Text = "• ..."`) continua nos ~50 pontos das telas: `Errors` virou um canal do console. Nenhuma tela de lógica foi reescrita.
- **Níveis, sem mexer no Core:**
  - Aviso: `_build.Warnings` (avisos do programa).
  - Conferir: T em conflito com o banco ativo (`ToolBankPick.Conflict` e `LatheConflict`) e mensagens de coisa a olhar (ex.: "Novo descarta o trabalho aberto", operações que ficaram Incompletas, face não achada no STEP, bruto STEP que o simulador não desenha).
  - Info: andamento (lendo o STEP, STEP lido em X s, operação criada, trabalho novo, programa enviado ao Code).
  - Erro: o resto das mensagens das telas (valor inválido, DXF sem perfil, STEP não lido, não salvou...). A classificação das mensagens das telas é por palavras-chave num lugar só (`MessageChannel.Classify`); o que não reconhece vira Erro, o mais seguro.
- Os textos não mudaram. A linha de pendências, o motivo do Gerar travado e o "Ir ao campo" (JobBar) não foram tocados.
- Arquivos: `AvisosConsole.cs` (novo), `JobView.axaml(.cs)` e `LatheJobView.axaml(.cs)`.

## Testes
Core não mudou: 2087 verdes (inclui os 8 G-codes). Nenhum teste alterado. Build Release do App sem avisos. O App não tem projeto de teste.

## Conferido na tela (App no Linux em tela virtual, 1280 de largura)
- Fresa: console vazio ("Avisos · nenhum"); depois de Novo, linha Info "Trabalho novo…" com contagem. Torno: console vazio no mesmo lugar. Nenhuma linha laranja sob o 3D; a coluna da árvore ficou sem texto vermelho.
- **Não conferido:** console aberto com Aviso e Conferir de verdade (precisa de trabalho com T em conflito com o banco); recolher e abrir por clique; muitos avisos (a lista rola a partir de ~92 px); 125% e 150%; Windows.

## Para o Igor testar no .exe
1. Abra um trabalho com T diferente do banco: aparece como Conferir, no console, sem linha laranja sob o 3D.
2. Clique no cabeçalho do console: recolhe e abre; recolhido, mostra a contagem.
3. Faça "Novo", leia STEP, crie uma operação pelo 3D: as mensagens aparecem como Info/Conferir/Erro no console, e a coluna da árvore fica limpa.
4. Gerar travado: motivo e "Ir ao campo" na barra de baixo como antes.

## Decisões e avisos
- Nível das mensagens das telas é por palavra-chave (texto vem de ~50 lugares); mensagem nova de tela entra como Erro até alguém incluí-la na lista. Se preferir, a P4 pode trocar isso por nível explícito em cada ponto.
- O console ocupa ~24 px recolhido ou vazio e até ~120 px com avisos; o 3D perde esse espaço na altura, não na largura.
