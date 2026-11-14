# UX-04h-E: plano do armário de ferramentas (PLANO, sem código), 14/11/2026

Estado do conteúdo técnico: rascunho (só o Igor marca revisado).
Base: `origin/dev` em `267c893` (#179). Só leitura de código. Nada foi rodado nem testado.
O que não li no código está escrito "não conferido".

**Modelo do Igor (14/11):** armário (todas as ferramentas, sem T) → magazine (as montadas, cada uma com T) → lista do
programa (as usadas). O T é a posição no magazine: não é fixo e não identifica a ferramenta.

**Regras que este plano não quebra:** os 8 G-codes de referência não mudam. O `.casillas-job` antigo abre igual, não ganha
ligação com o banco e não sobe de versão. Web e nuvem só anotadas (dependem da conta única, PLAT-03).

## 1. O que existe hoje

Caminhos a partir de `src/CncStudio.Core/`, salvo quando dito.

**Ferramenta do banco** (`BankTool`, `Cam/ToolBank.cs:80-122`). Só o T é obrigatório (`Number`, `:80`). O resto pode faltar:
nome, tipo, Ø, arestas, comprimento de corte, comprimento total, suporte Ø e comprimento, raio de canto, ângulo da ponta,
largura do inserto, código ISO, raio de ponta, material, rpm, avanço XY, avanço Z (`:82-108`). Mais duas marcas:
"a conferir" (`:111`) e ativa/inativa (`:114`). **Não existe id da ferramenta.** Quem a identifica é o T.

**Banco** (`ToolBank`, `Cam/ToolBank.cs:133`): id (vira o nome do arquivo), nome, máquina (fresa ou torno) e a lista.

**Regras do `ToolBank.Validate`** (`:203-230`) e do `ToolError` (`:233-290`):
- T repetido no banco: erro "T… repetido no banco" (`:218-221`).
- Faixa do T: fresa 1 a 9999, torno 1 a 99 (`:140`, `:237-240`).
- O tipo precisa ser da máquina do banco (`:243-246`). Medidas, se informadas, maiores que zero (`:253-257`).
- Na leitura, qualquer erro do `Validate` recusa **o arquivo inteiro** (`Cam/ToolBankJson.cs:115-118`). A pasta segue sem
  esse banco e mostra o motivo (`Cam/ToolBankStore.cs:35-38`).

**Arquivo** (`Cam/ToolBankJson.cs`): `<id>.casillas-ferramentas`, formato `casillas-ferramentas`, versão 1 (`:22-24`).
Campo sem valor não é gravado. Chave desconhecida é ignorada na leitura. Versão mais nova abre só para leitura (`:104`, `:120`).

**Pasta e gravação** (`Cam/ToolBankStore.cs`): um arquivo por banco em `%APPDATA%\CncStudio\ferramentas`
(`src/CncStudio.App/Settings/AppSettings.cs:123-124`). Grava em arquivo temporário e troca no fim (`:58-77`).
Banco ativo no `config.json`: `ActiveMillBankId` e `ActiveLatheBankId` (`AppSettings.cs:108`, `:114`).

**Quem lê o banco, sempre por T:**
- Simulador e análise do Code: `ToolBankLookup.Tools` junta o banco ativo do torno e, por cima no mesmo T, o da fresa
  (`Cam/ToolBankLookup.cs:25-38`).
- Escolha na operação da fresa (B2): lista as ativas do banco ativo (`Cam/ToolBankPick.cs:70-71`). Escolher **copia** T, Ø e
  nome para a operação (`:159-183`). Depois a ligação é só por T, Ø e nome (`Conflict`, `:190-217`; `SameTool`, `:229`).
- Escolha no torno (D1): copia T, nome e rpm (`ApplyLathe`, `:133-134`).
- "Adicionar ao banco ativo": T já usado recusa e oferece o próximo livre (`AddToBank`, `:236-269`).

**O que o trabalho guarda** (não muda neste plano): fresa `CamTool` = T, Ø, rpm, avanços e nome (`Cam/ToolPath.cs:26`;
`Cam/CamJobJson.cs:304-311`). Torno `LatheTool` = T, nome, velocidade de corte, rpm máx. e rpm (`Cam/Lathe.cs:7`;
`Cam/LatheJobJson.cs:79-82`). `.casillas-job`: fresa na versão 3 (`Cam/CamJob.cs:128`), torno na versão 1 (`Cam/LatheJob.cs:64`).

**Pacote `.casillas-cam`** (versão 1, `Cam/CamHandoff.cs:131`): leva hash do programa, controle, máquina, perfil, programa nº,
G5x, zero, **fixação**, bruto e ferramentas (`Cam/CamHandoffJson.cs:23-41`). Cada ferramenta: T, Ø, nome, rpm e avanços do
trabalho, mais tipo, raio de canto, ângulo da ponta, comprimento de corte e suporte quando o T está no banco ativo sem
conflito (`CamHandoff.FromBank`, `CamHandoff.cs:195-211`; JSON em `CamHandoffJson.cs:92-106`). A chave "banco" leva só o
**nome** do banco, informativo, e não é gravada sem banco (`CamHandoffJson.cs:42-46`). Versão mais nova: o Code ignora o
pacote com aviso (`:135-138`). Só a fresa envia pacote: nos arquivos `LatheJobView*.cs` não há `CamHandoff`.

**Folha de setup** (`Reports/SetupSheet.cs`, `Reports/SetupSheetHtml.cs`): arquivo, local, programa, comando, máquina, zeros
de peça, linhas, movimentos, **bruto** (texto), limites, verificação (`SetupSheetHtml.cs:104-119`) e a tabela de ferramentas:
T, operação/ferramenta, Ø (só fresa), 1ª linha, alcance, avanço e tempos (`:146-147`). O Ø e a descrição vêm do banco ativo,
por T (`src/CncStudio.App/MainWindow.axaml.cs:833`, `:850-851`); o comentário do programa vale por cima (`SetupSheet.cs:82`).
Na web a folha sai sem banco (`src/CncStudio.Web/MainView.axaml.cs:519`).

**A folha de setup NÃO leva fixação.** O registro `SetupSheet` não tem campo de fixação (`SetupSheet.cs:32-50`) e o HTML não
tem linha de fixação. A fixação existe em três lugares, e nenhum chega à folha:
- Trabalho da fresa: `JobFixture`, tipo e descrição em texto (`Cam/CamJob.cs:34`, `:168`). Vai no `.casillas-job`
  (`CamJobJson.cs:99`) e no pacote (`CamHandoffJson.cs:36-38`). O Code lê o pacote (`:154`), mas não achei uso da fixação
  em `MainWindow.CamHandoff.cs` nem em `Cam/CamHandoffCode.cs`.
- Trabalho do torno: `LatheFixation`, tipo e três medidas opcionais (`Cam/LatheFixation.cs:12`; `Cam/LatheJob.cs:80`;
  `LatheJobJson.cs:56`). Fica só no `.casillas-job` do torno.
- Setup do programa no Code: `SetupFixture`, morsa, grampo ou placa, com medidas, desenhada no 3D
  (`Setup/MachiningSetup.cs:29`, `:269-294`).

**Tela Ferramentas** (`src/CncStudio.App/Modules/ToolBankView.cs`): bancos, "+ Nova ferramenta", Duplicar, Excluir,
Exportar banco, Importar banco (`:72`, `:83`), busca por T, nome ou Ø (`:70`), aba de inativas (`:252`). A disposição na tela
não foi conferida (li só os textos).

## 2. Identidade da ferramenta sem T

**Proposta:** cada ferramenta do armário ganha um **id próprio**, criado pelo programa quando ela nasce (do mesmo jeito que o
id do banco, `ToolBank.NewId`, `ToolBank.cs:143`). O id nunca muda e não aparece na tela. Nome, tipo e medidas são só dados.

Uma linha do armário é **um cadastro** (um modelo de ferramenta, por exemplo "Fresa topo Ø10 Z4"). Não é uma peça física
contada. Quantidade em estoque fica fora deste plano.

**Os dois riscos do D1** (`docs/tarefas/UX-04h-D1-volta.md:40-41`):
- *Nome editado vira outra ferramenta:* resolve. Editar o nome não troca o id; continua a mesma, onde estiver montada.
- *Duas iguais em posições diferentes contam como uma:* resolve do lado certo. São a mesma linha do armário, montada em duas
  posições. Cada posição é uma linha do magazine, com o seu T. Nada se perde nem se junta por engano.

**O que o id não resolve:** o `.casillas-job` continua guardando só T, Ø e nome. A ligação do trabalho com o armário segue
sendo por T (via magazine ativo) com conferência de Ø e nome, como hoje. Trabalho antigo não ganha id.

## 3. Ligação armário → magazine

**Proposta: o magazine guarda referência.** Cada posição do magazine é "T + id da ferramenta". Os dados (nome, Ø, tipo…)
ficam só no armário. Motivo: uma fonte de verdade por dado (convenção do `CLAUDE.md`). Ver a Decisão 2.

Ao abrir, o Core junta magazine e armário e entrega o mesmo `ToolBank` de hoje, em memória. Assim o simulador, a escolha na
operação, o pacote e a folha de setup continuam lendo como leem.

| Ação | O que acontece |
|---|---|
| Editar no armário uma ferramenta montada | Muda em todos os magazines onde está, na hora. Os trabalhos já salvos não mudam (têm a cópia deles); o aviso do B2 mostra a diferença de Ø ou nome. |
| Apagar no armário uma ferramenta montada | Recusa e diz onde está ("montada no Magazine X, T3"). Primeiro desmontar. Para tirar de uso sem apagar: Inativa. (Decisão 4) |
| Montar a mesma em duas posições | Pode (ferramenta gêmea). As duas posições mostram a mesma ferramenta. (Decisão 3) |
| Trocar o T | Muda só a posição no magazine. A ferramenta e o id ficam iguais. T ocupado: recusa e mostra quem está lá. |
| Desmontar | Sai do magazine. Continua no armário. |
| Id do magazine não existe no armário | A posição aparece como "ferramenta não encontrada no armário", com o T reservado. Nada é inventado. |

Regras do magazine que ficam: T único; fresa 1 a 9999, torno 1 a 99; só tipo da máquina do magazine.
"A conferir" e Ativa/Inativa passam a ser da ferramenta (armário). Inativa não é oferecida na operação, como hoje.

Programa já gerado não muda quando o T troca: o G-code e o trabalho levam o T antigo. O aviso do B2 aparece ao abrir o trabalho.

## 4. Onde e como grava

- **Armário:** um arquivo só, `armario.casillas-armario`, formato novo, versão 1, na mesma pasta
  `%APPDATA%\CncStudio\ferramentas`. Fresa e torno juntos: a broca já serve aos dois (`ToolBank.cs:48`) e o tipo diz de qual
  máquina é. A tela filtra por máquina. (Decisão 5)
- **Magazine:** um arquivo por magazine, `<id>.casillas-magazine`, formato novo, versão 1: id, nome, máquina e a lista
  "T + id da ferramenta". O id do magazine é o id do banco de hoje, então `ActiveMillBankId` e `ActiveLatheBankId` continuam
  valendo sem mexer. (Decisão 6)
- **Gravação:** arquivo temporário e troca no fim, como hoje (`ToolBankStore.Save`). Versão mais nova: só leitura, como hoje.
- **Bancos de hoje (`.casillas-ferramentas` versão 1):** não são regravados nem apagados e o formato não sobe de versão.
  Ficam parados na pasta. Se o Igor voltar para um .exe antigo, ele ainda os abre.
- **Migração (uma vez, com marca própria no `config.json`, como a `LatheToolsMigrated`):**
  1. Cada banco vira um magazine com o mesmo id, nome e máquina, e os mesmos T.
  2. Cada ferramenta de cada banco vira uma linha do armário com id novo, com todos os campos como estão
     (ausente continua ausente; "a conferir" e inativa vão junto).
  3. Duas ferramentas com **todos** os campos iguais (fora o T) viram uma linha só, montada nos dois lugares. Qualquer
     diferença: duas linhas. Nada é juntado por nome parecido. (Decisão 7)
  4. A marca só é gravada se armário e magazines gravaram. Sem a marca, tenta de novo na próxima abertura.
  5. Banco que não abre hoje (arquivo recusado) não migra; o aviso de hoje continua.
- **Continuar abrindo os arquivos de hoje:** "Importar banco" segue aceitando `.casillas-ferramentas` versão 1. O arquivo vira
  um magazine novo e as ferramentas dele entram no armário pela regra 3.
- **Web e nuvem:** só anotado. A web não usa o banco hoje (não há `ToolBank` em `src/CncStudio.Web`). Depende do PLAT-03.

## 5. Lista do programa e folha de setup

**Lista do programa (`.casillas-cam`).** Proposta: cada ferramenta do pacote ganha uma chave opcional com o id do armário,
gravada só quando o T está no magazine ativo sem conflito (a mesma condição do `FromBank`). Sem magazine ou com conflito: a
chave não é gravada. O leitor de hoje lê só as chaves que conhece (`CamHandoffJson.cs:210-221`), como aconteceu com "banco".
**Subir ou não a versão do `.casillas-cam` é decisão do Igor** (Decisão 8). O `.casillas-job` não muda.

Para que serve o id no pacote: o Code pode buscar a ferramenta no armário mesmo que o T dela tenha mudado no magazine depois
do envio. Sem o id, a busca segue por T.

**Folha de setup com fixação.** Hoje não leva (item 1). De onde a fixação pode vir:
- **Fresa:** do pacote, que já traz tipo e descrição. Entra uma linha "Fixação" na folha quando o pacote confere com o
  programa (hash). Programa editado ou sem pacote: a linha não aparece. Não muda formato nenhum. (Decisão 9)
- **Setup do programa no Code** (morsa, grampo, placa com medidas): pode entrar na mesma linha, com nome e tipo. (Decisão 9)
- **Torno:** a fixação do torno fica só no `.casillas-job`, e o torno não envia pacote. Para chegar à folha, o torno precisa
  passar a enviar pacote. É maior e fica fora das fatias, à espera do Igor. (Decisão 10)

A lista de ferramentas da folha continua por T, com Ø e nome do magazine ativo. Não muda de aparência.

## 6. Telas (só descrição; mínimo 1280×720)

**Tela Ferramentas.** No topo, duas partes: **Armário** e **Magazines**.
- *Armário:* lista sem coluna T. Colunas: nome, tipo, Ø, estado (completa, incompleta, a conferir, inativa) e "montada em"
  (por exemplo "Fresa 1: T3, T12"). Filtro Fresa / Torno / Todas e a busca de hoje. Botões: + Nova ferramenta, Duplicar,
  Excluir (recusa se montada). O painel de detalhe é o de hoje, sem o campo T.
- *Magazines:* à esquerda, a lista de magazines (o que hoje é a lista de bancos). No meio, as posições em ordem de T: T, nome,
  tipo, Ø, estado. Botões: **Montar ferramenta**, Desmontar, Trocar T, Exportar, Importar.
- *Montar ferramenta:* abre a lista do armário, já filtrada pela máquina do magazine, com busca. Escolhe a ferramenta e
  digita o T. O campo T nasce vazio; ao lado, o texto "próximo livre: T7". T ocupado: recusa e mostra quem está lá.
- Ferramenta nova a partir do magazine: "Nova ferramenta e montar" cria no armário e já pede o T.

**Escolha na operação da fresa (B2).** A lista continua sendo a do magazine ativo ("T3 · nome · Ø10"). Muda:
- O seletor "Banco ativo" passa a se chamar "Magazine ativo" (Decisão 11). Mesmo lugar e mesma largura: nada novo na faixa.
- No fim da lista, a linha "Montar outra do armário…" abre o diálogo de montar. Depois de montar, a ferramenta já vem escolhida.
- "Adicionar ao banco ativo" vira "Adicionar ao armário e montar": cria no armário, "a conferir", e monta no T digitado.
  T ocupado: recusa e oferece o próximo livre, como hoje.

**Trabalho do torno (D1).** As mesmas três mudanças. Escolher continua copiando T, nome e rpm.

Em 1280×720: nenhum controle fixo novo na faixa. Larguras da tela Ferramentas nova: não conferidas (precisa de foto na fatia).

## 7. O D3 (segurado) e as pendências do B2

- **D3 (importar para um banco existente):** **muda.** A parte "conflito de T à vista" some, porque o armário não tem T.
  Vira "Importar ferramentas para o armário": mostra a lista antes de gravar (nova ou igual) e só grava no "Importar".
  Igual pula. Importar um banco inteiro como magazine continua como é. Está na fatia E6.
- **Decisão 5 do plano D (T em conflito ao importar):** deixa de existir pelo mesmo motivo.
- **Pendências do B2** (`docs/tarefas/UX-04h-B2-volta.md:51-56`): as três **continuam iguais**, o armário não resolve nenhuma.
  - Ferramenta sem Ø é oferecida: continua. A regra fica no mesmo lugar (`ToolBankPick.Choices`).
  - Layout em 1280 (o seletor tira ~170 px): continua. Este plano não piora (não põe nada novo na faixa).
  - Operação nova vazia trava o Salvar: continua. É regra do CAM-12, não do banco.

## 8. Decisões do Igor

1. **Uma linha do armário é o quê?**
   a) Um cadastro (modelo de ferramenta), sem contar peças. **(recomendo)**
   b) Uma peça física: duas fresas iguais são duas linhas.
   c) Um cadastro com campo "quantidade" (estoque). Fica maior.
2. **O magazine guarda referência ou cópia?**
   a) Referência: T + id. Os dados ficam só no armário; editar lá vale em todo lugar. **(recomendo)**
   b) Cópia + id: o magazine guarda os dados também, com um botão "Atualizar do armário". Dois lugares para o mesmo dado.
   c) Cópia sem ligação: montar copia e esquece. Volta o risco do nome editado.
3. **A mesma ferramenta em duas posições do mesmo magazine?**
   a) Pode (ferramenta gêmea). **(recomendo)**
   b) Não pode: recusa e mostra onde ela já está.
4. **Apagar no armário uma ferramenta montada?**
   a) Recusa e diz onde está; primeiro desmontar. **(recomendo)**
   b) Pergunta e desmonta de todos os magazines junto.
5. **Um armário ou dois?**
   a) Um só, com filtro Fresa / Torno. **(recomendo: a broca serve aos dois)**
   b) Dois arquivos, um de fresa e um de torno.
6. **Arquivo do magazine?**
   a) Arquivo novo `.casillas-magazine`; os bancos de hoje ficam parados, sem regravar. **(recomendo: o .exe antigo ainda abre os dele)**
   b) O mesmo `.casillas-ferramentas`, subindo para a versão 2. O .exe antigo passa a abrir só para leitura.
7. **Migração: quando juntar duas ferramentas numa linha do armário?**
   a) Só quando todos os campos são iguais (fora o T). **(recomendo)**
   b) Nunca: cada ferramenta de cada banco vira uma linha. O Igor junta à mão depois.
   c) Por nome e tipo iguais, marcando "a conferir". Pode juntar o que não é igual.
8. **O `.casillas-cam` leva o id do armário?**
   a) Sim, em chave opcional, e a versão continua 1. **(recomendo)**
   b) Sim, e a versão sobe para 2. O Code antigo ignora o pacote novo, com aviso.
   c) Não leva. A ligação segue só por T.
9. **Fixação na folha de setup (fresa)?**
   a) Linha "Fixação" com o que vem do pacote, e as fixações do setup do programa, se houver. **(recomendo)**
   b) Só a do pacote.
   c) Não agora.
10. **Fixação do torno na folha?**
    a) Depois: pede o torno enviar pacote, que é uma missão própria. **(recomendo)**
    b) Agora, como fatia a mais (grande).
11. **Nome na tela?**
    a) "Armário" e "Magazine"; "Banco ativo" vira "Magazine ativo". **(recomendo: é o modelo do Igor)**
    b) Manter "Banco" onde hoje é banco e só acrescentar "Armário".
12. **Ordem com o que está aberto no plano D?**
    a) D2 (tirar a tabela antiga) antes do E1; D3 é trocado pelo E6; D4 (web) espera. **(recomendo)**
    b) Começar o E1 já e deixar o D2 para depois.

## 9. Fatias em PRs

Cada PR sai do `dev`, com o teste mestre (8 G-codes) verde antes e depois. Nenhum muda G-code nem `.casillas-job`.

**E1. Armário e magazine no Core, migração e leitura (grande).**
Muda: tipos e arquivos novos no Core (armário, magazine, JSON, validação, junção em `ToolBank`), migração única, o App passa a
ler e gravar pelos arquivos novos. As telas ficam iguais às de hoje.
Teste do Igor no .exe:
1. Abrir com a pasta de hoje. Ver os mesmos bancos, as mesmas ferramentas e os mesmos T na tela Ferramentas.
2. Na pasta `%APPDATA%\CncStudio\ferramentas`: os `.casillas-ferramentas` continuam lá, e apareceram o armário e os magazines.
3. Editar o nome de uma ferramenta, fechar e abrir: o nome ficou.
4. Abrir um programa de fresa e um de torno: Ø e desenho iguais aos de antes.
5. Abrir um `.casillas-job` antigo: abre igual, com os mesmos avisos.

**E2. Tela do armário (médio).**
Muda: parte "Armário" na tela Ferramentas (lista sem T, "montada em", criar, editar, duplicar, excluir com recusa).
Teste do Igor no .exe:
1. Abrir Armário: ver todas as ferramentas, sem coluna T, com "montada em".
2. Criar uma ferramenta nova: ela aparece no armário e em nenhum magazine.
3. Editar o Ø de uma ferramenta montada: o magazine mostra o Ø novo.
4. Tentar excluir uma montada: recusa e diz onde está.
5. Conferir a tela em 1280×720.

**E3. Montar no magazine (médio).**
Muda: parte "Magazines" com Montar ferramenta, Desmontar e Trocar T.
Teste do Igor no .exe:
1. Montar uma ferramenta do armário num T livre.
2. Tentar montar num T ocupado: recusa e mostra quem está lá.
3. Montar a mesma ferramenta em outro T (se a Decisão 3 for "pode").
4. Trocar o T de uma ferramenta e desmontar outra: as duas continuam no armário.
5. Abrir um programa que usa esse T: o Ø do simulador é o da ferramenta montada.

**E4. Escolha na operação, fresa e torno (médio).**
Muda: "Magazine ativo", "Montar outra do armário…", "Adicionar ao armário e montar"; textos dos avisos.
Teste do Igor no .exe:
1. Trabalho fresa: escolher uma ferramenta do magazine ativo. T, Ø e nome copiados, como hoje.
2. "Montar outra do armário…": montar e ver a ferramenta já escolhida.
3. Digitar uma ferramenta à mão e "Adicionar ao armário e montar": aparece no armário, "a conferir", e no magazine.
4. Trabalho do torno: os mesmos três passos (copia T, nome e rpm).
5. Gerar o G-code de um trabalho antigo: igual ao de antes.

**E5. Id do armário no `.casillas-cam` (pequeno; só se a Decisão 8 for a ou b).**
Muda: uma chave por ferramenta no pacote e a leitura dela no Code.
Teste do Igor no .exe:
1. Enviar ao Code um trabalho com ferramenta do magazine: abre e simula igual.
2. Abrir o `.casillas-cam` no Bloco de Notas: a ferramenta do magazine tem o id; a digitada à mão não tem.
3. Abrir um `.casillas-cam` antigo: abre igual, sem aviso novo.

**E6. Importar e exportar o armário (médio; troca o D3).**
Muda: "Importar ferramentas para o armário" (lista antes de gravar), exportar o armário, importar banco antigo como magazine.
Teste do Igor no .exe:
1. Exportar o armário e importar de volta: todas aparecem como "igual" e nada duplica.
2. Importar um `.casillas-ferramentas` antigo: vira um magazine novo e as ferramentas entram no armário.
3. Importar um arquivo estragado: recusa com o motivo e nada muda.
4. Importar um arquivo de versão mais nova: recusa pedindo para atualizar.

**E7. Fixação na folha de setup da fresa (pequeno; só se a Decisão 9 for a ou b).**
Muda: linha "Fixação" na folha (Core, com teste) e a passagem do pacote para a folha no App.
Teste do Igor no .exe:
1. Enviar ao Code um trabalho com fixação "Morsa": a folha de setup mostra a linha Fixação.
2. Editar o programa e gerar a folha de novo: a linha some (o pacote não confere mais).
3. Programa sem pacote: folha igual à de hoje.

Ordem: E1 → E2 → E3 → E4 → E6. O E5 e o E7 são soltos e podem entrar depois do E1.
Fora das fatias: fixação do torno na folha (Decisão 10), web e nuvem (PLAT-03), estoque (Decisão 1c).

## 10. Riscos e o que não muda

**O que pode estragar:**
- **Banco:** o armário é um arquivo só. Se ele estragar, todos os magazines ficam com "ferramenta não encontrada". Defesas:
  gravação por arquivo temporário; os bancos antigos ficam na pasta, sem mexer; exportar o armário (E6).
- **Migração pela metade:** a marca só grava se tudo gravou. Rodar a migração duas vezes não pode duplicar o armário: o E1
  precisa de teste para isso.
- **Juntar errado na migração:** com a Decisão 7a só junta o que é idêntico. Com a 7c pode juntar duas ferramentas diferentes.
- **Voltar para um .exe antigo:** ele abre os bancos antigos, parados na data da migração. O que foi feito depois, no
  armário, ele não vê. Com a Decisão 6b ele abriria só para leitura.
- **Programa:** editar no armário uma ferramenta montada muda o Ø que o simulador usa para **todo** programa que chama aquele
  T, inclusive os antigos. Já é assim hoje ao editar o banco; com a referência, a mudança alcança mais magazines de uma vez.
- **Simulação:** trocar o T ou o magazine ativo muda o Ø e o desenho dos programas que usam aquele T. Já é assim hoje.
- **Mesmo T na fresa e no torno:** a fresa continua valendo por cima (`ToolBankLookup.cs:25-38`). Este plano não muda isso.
- **Pacote:** com a Decisão 8b, o Code antigo ignora o pacote novo (com aviso) e o programa abre sem bruto, zero e ferramentas do CAM.

**O que não muda:**
- Os 8 G-codes de referência e o teste mestre.
- O G-code gerado pelo CAM: o T, o Ø e os avanços saem da cópia do trabalho, não do banco.
- O `.casillas-job`, da fresa e do torno: mesmo formato, mesma versão, sem id do armário. Antigo abre igual.
- O formato `.casillas-ferramentas` versão 1 e os arquivos que o Igor já tem.
- O setup do programa (`MachiningSetup`), o Assist e a tabela antiga (essa é do D2).
- A regra "nada suposto": campo ausente continua ausente no armário, no magazine e no pacote.

**Não conferido:**
- Nada rodado: nem build, nem teste, nem .exe, nem máquina.
- Como o teste mestre monta as ferramentas (se lê algum banco).
- A disposição e as larguras da tela Ferramentas em 1280×720.
- `Cam/CamHandoffCode.cs` por inteiro (li só a busca por fixação) e o que o Code mostra do pacote na tela.
- O Assist e a tabela antiga do `config.json` (ficaram como o plano D descreveu).
- O `config.json` e os bancos reais do Igor.
- A web e a nuvem.
