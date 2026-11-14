# UX-04h PR B2 — Volta (14/11/2026)

```
ID: UX-04h (PR B2)
O que foi feito (até 5 linhas):
  O Trabalho fresa deixou a tabela antiga e escolhe a ferramenta no banco ativo (seletor "Banco ativo" fixo à direita da faixa).
  Escolher copia T, Ø e nome; rotação e avanços só se cadastrados; tipo e medidas do banco aparecem só para leitura ("No banco: ...").
  Operação nova nasce com T, Ø, rotação e avanços vazios. Aviso de T em conflito (só avisa). "Adicionar ao banco ativo" cria "a conferir".
  Regras no Core (Cam/ToolBankPick.cs, com teste). G-code, CamTool, CamJobJson, gerador e CamPost sem mudança; .casillas-job igual.
Arquivos alterados/criados:
  Core: src/CncStudio.Core/Cam/ToolBankPick.cs (novo). Testes: tests/CncStudio.Core.Tests/ToolBankPickTests.cs (novo).
  App: Modules/JobView.axaml(.cs), Modules/JobView.Props.cs, Modules/CamView.axaml.cs, Modules/ToolBankView.cs (SaveFromJob),
  MainWindow.ToolBank.cs, Settings/AppSettings.cs (ActiveMillBankId). Docs: docs/ESTADO.md, esta Volta, fotos em docs/tarefas/UX-04h-B2/.
Testes: rodados (dotnet test, Release, local) / 1823 passaram, 0 falharam / CamJobGoldenTests 8 de 8 antes de mudar código
  (commit f15f40e) e em todos os commits seguintes. Web compila (Release). Testes novos (18): escolher, inativa, "só cadastro",
  operação nova vazia, aviso, adicionar (repetido e sem T livre), banco carregado/vazio/em conflito nos 7 trabalhos de fresa
  (mesmo programa e mesmo arquivo), escolher não põe campo novo no arquivo.
Banco ativo: onde fica guardado e prova de que o .casillas-job não mudou:
  AppSettings.ActiveMillBankId no config.json (%APPDATA%\CncStudio). Sem escolha, ou banco apagado: o mesmo do simulador
  (ToolBankLookup.Primary: "Ferramentas fresa" migrado, senão o 1º por nome). Prova: teste Banco_carregado_ou_vazio_nao_muda...
  (7 trabalhos: JSON idêntico, versão igual) e Escolher_no_banco_nao_poe_campo_novo_no_arquivo; no roteiro headless, depois de
  escolher, avisar e recusar, o JSON do trabalho aberto saiu idêntico e sem o id do banco. Persistência: ida e volta do campo
  pelo mesmo serializador do config.json (não abri e fechei o App de verdade; ver "Não conferido").
Aviso de T em conflito: o que compara e como aparece:
  Compara o que a operação usa da ferramenta: Ø e nome (nome só quando o banco tem nome). Rotação e avanços não entram (são da
  operação; avisariam sempre que o usuário ajusta o corte). Inativa no banco diz "(inativa no banco)". Aparece em âmbar no painel
  da operação e na caixa de avisos abaixo do 3D (operações ligadas), ex.: "T1 no banco "Ferramentas fresa" é diferente do trabalho:
  Ø: trabalho 10 × banco 12. O trabalho usa a cópia dele." Não bloqueia; nada é atualizado sozinho. Substitui o aviso antigo
  (Ø diferente na tabela antiga).
Adicionar ao banco: comportamento nos casos de T repetido e sem T livre:
  Botão "Adicionar ao banco ativo" aparece com T digitado fora do banco ou diferente dele (T vazio ou igual: sem botão).
  Cria só com os campos que têm valor (vazio fica ausente; tipo ausente), marca "a conferir". T repetido: "✕ T1 já existe no banco
  "Ferramentas fresa"." + botão "Adicionar como T4" (próximo livre; a dica diz que a operação passa a usar T4). Sem T livre:
  "... Não há T livre neste banco (vai de 1 a 9999)." sem botão. Banco de versão mais nova (só leitura): recusa com o motivo.
Torno: o que seria igual e o que muda (sem mexer):
  Igual: banco ativo por máquina (ActiveLatheBankId), escolha só de ativas, operação nova vazia, aviso e "Adicionar" com
  "a conferir". Muda: LatheTool tem outros campos (tipo do inserto, largura do bedame, raio de ponta); faixa de T 1 a 99 (o "sem T
  livre" aparece muito antes); tipos "só cadastro" não existem no torno; o Trabalho do torno ainda lê/grava a tabela antiga
  (ToolsSource), que continua só por ele.
Fotos 1280×720 (arquivos em docs/tarefas/):
  UX-04h-B2/01-operacao-nova-vazia, 02-escolher-ferramenta (lista aberta: T2 inativa fora, T5 "só cadastro"), 03-escolhida-T3,
  04-aviso-conflito, 05-adicionar-recusa, 06-adicionado-T4. Roteiro headless fora do repositório: trilho de 76 px + CamView
  (o mesmo espaço que a MainWindow dá ao CAM), banco só na memória (nada gravado no seu %APPDATA%).
Decisões tomadas: (só as autorizadas)
  Nenhuma fora da seção 2. Propostas (Igor ajusta): local do banco ativo (config.json), rótulo "BANCO ATIVO" à direita da faixa,
  campos do aviso (Ø e nome).
Pendências e dúvidas: (A definir)
  1. "Escolher preenche tipo e medidas": o CamTool só guarda T, Ø, rotação, avanços e nome, e não pode mudar. Tipo e medidas
     aparecem só para leitura, lidos do banco pelo T; não ficam no trabalho. Se tiverem de ficar, é mudança de formato (A definir).
  2. "Adicionar como T{livre}" troca o T só da operação selecionada; outras operações com o mesmo T continuam com o aviso.
  3. Ferramenta sem Ø no banco é oferecida e deixa o Ø vazio na operação (o B1 dizia "Ø exigido para escolher"; segui o teste 2).
Riscos ou contradições encontradas:
  - Banco ativo × B1: o simulador do Code continua lendo o banco "principal" (Primary), não o ativo. Com outro banco ativo, o Code
    pode simular com outro Ø. Não decidi; o padrão do ativo é o mesmo banco do simulador.
  - Operação nova vazia trava o Salvar do trabalho até escolher/digitar a ferramenta (regra do CAM-12: campo vazio bloqueia Salvar).
  - O seletor fixo à direita tira ~170 px da faixa: em 1280 a faixa rola e "Mandrilhar"/"4º eixo" ficam após a rolagem.
  - "Enviar ao Code" não grava mais as ferramentas do trabalho na tabela antiga (ninguém lê mais a tabela na fresa).
  - Defeito achado e corrigido no roteiro: trocar o banco no seletor travava (lista refeita dentro do próprio evento).
Não conferido: .exe; abrir e fechar o App de verdade com o banco ativo; outras resoluções; tela com bancos/arquivos reais; Prévia.
O que o Igor deve testar na prévia:
  Criar Bolsão (campos vazios) → escolher T do banco; inativa fora, "só cadastro" marcada; abrir trabalho antigo com T fora do banco ou
  outro Ø (aviso, Gerar igual); "Adicionar ao banco ativo" com T repetido; trocar o banco ativo, fechar e abrir o App.
Próximo passo sugerido (1): decidir se o simulador do Code passa a ler o banco ativo (risco 1) antes do PR D.
```
