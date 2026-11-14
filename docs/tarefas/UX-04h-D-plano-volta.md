# Volta UX-04h (passo D, PLANO), 14/11/2026

Estado do conteúdo técnico: rascunho (só o Igor marca revisado).
Fase 2 de 4: só o plano. Nenhum código de produto neste ramo. Base: `origin/dev` em `e9a58cc` (#173).
Nada foi testado em máquina. Nada aqui foi rodado: é leitura de código.

## 1. O que foi encontrado

### Parte 1, torno

**a. Tabela antiga.** `AppSettings.Tools` (`src/CncStudio.App/Settings/AppSettings.cs:96`), lista de `ToolEntry` (`:29`):
T, Ø (padrão 10), descrição, tipo (`Kind`, texto), largura do inserto (padrão 3), rpm, avanço XY, avanço Z.
Nenhuma tela edita mais essa lista desde o B1. Quem ainda usa (lista completa):

| Quem | Onde | Lê | Grava |
|---|---|---|---|
| Trabalho do torno, escolha | `MainWindow.Modules.cs:77` → `CamView.ToolsSource` → `LatheJobView.BankPicker` (`LatheJobView.Props.cs:440`) | sim (só tipos de torno) | não |
| Trabalho do torno, Enviar ao Code | `LatheJobView.EnsureToolsInBank` (`LatheJobView.axaml.cs:662`, chamado em `:97`), grava por `Cam.SaveTools` (`MainWindow.Modules.cs:78`) | sim | **sim** (T, nome, tipo, rpm; Ø fica 10) |
| Assist, plano 2D | `Assist.PlanTools` (`MainWindow.Modules.cs:161`) → `PlanInput.Tools` (`Core/Assist/Planner.cs`) | sim (a lista inteira, sem filtro) | não |
| Migração única do B1 | `MainWindow.ToolBank.cs:13`, `AppSettings.LegacyToolRows` (`:118`), `ToolBankStore.MigrateOnce` | sim, uma vez | não |

**Achado 1 (ferramenta fora do banco).** Depois da migração do B1, o Enviar ao Code do torno continuou pondo ferramenta nova
só na tabela antiga. Essas ferramentas **não estão** no banco de torno. E o contrário: ferramenta criada na tela Ferramentas
(banco de torno) não aparece na escolha do Trabalho do torno.

**Achado 2 (Assist).** O Assist recebe a tabela inteira: uma linha de torno entra como "fresa Ø 10" (`IsDrill` falso, Ø padrão 10).
Já é assim hoje; o D não cria isso.

**b. `Primary` do torno** (`ToolBankLookup.Primary`, `Core/Cam/ToolBankLookup.cs:11`):
- `ToolBankLookup.Tools` (`:24`): Ø por T da análise e do simulador (`AppSettings.DiameterOf`, `ToMachineSettings`) e a folha de setup (`MainWindow.axaml.cs:832`).
- `ToolBankLookup.LatheSetupTools` (`:47`) → `AppSettings.WithToolKinds` (`:170`) → `Graphics.Setup` (`MainWindow.axaml.cs:707`): desenho da ferramenta do torno (tipo, Ø e inserto; 10 e 3 de reserva).

**c. Arquivo do banco de torno.** Já existe e é o mesmo da fresa: `<id>.casillas-ferramentas`, versão 1, `"machine": "torno"`
(`ToolBankJson`). T de 1 a 99. Campos próprios do torno já no banco: largura do inserto (bedames), código ISO, raio de ponta
(`BankTool`, `ToolBank.cs:80`). A tela Ferramentas já cria e edita banco de torno (`ToolBankView.cs:47-48`). O "Ferramentas torno"
(`ferramentas-torno`) nasce na migração do B1. O que a tabela antiga tinha de torno (T, nome, tipo, inserto, rpm) cabe todo no banco.
A operação de torno guarda `LatheTool` (`Core/Cam/Lathe.cs:7`): T, nome, velocidade de corte, rpm máx. e rpm. O banco não tem
velocidade de corte nem rpm máx.: continuam só na operação.

**e. Assist × tabela.** Confirmado: a tabela que o Assist lê é a mesma `AppSettings.Tools` que o D remove. Ver Decisão 1.

**f. O que muda.** Ver o plano (seção 2). O setup do programa (`MachiningSetup`/`SetupJson`) não é tocado.

### Parte 2, web

**a.** "Minhas ferramentas" ficam no navegador (`localStorage`, chave `cc.mytools.v1`, `src/CncStudio.Web/MainView.Setup.cs:18`).
É o JSON do setup só com a lista (`SetupJson.SerializeMyTools`), no máximo 20. Grava quando a caixa "Guardar esta lista como minhas
ferramentas" está marcada (`wwwroot/setup-ui.js:59`). Campos (`SetupTool`): tipo, T, Ø, raio de canto, ângulo da ponta,
comprimento de corte, suporte Ø e comprimento, largura do inserto. **Não tem nome, rpm nem avanços.**

**b.** O desktop não enxerga o `localStorage` do navegador e a web **não tem** botão de baixar essa lista (conferido em `setup-ui.js`,
`tools-ui.js` e `web.js`). O conversor já existe e tem teste: `ToolBankImport.FromWebMyTools` (`Core/Cam/ToolBankImport.cs:125`,
`ToolBankTests.cs:275`), mas nenhuma tela o chama. Qualquer caminho usável pelo cliente pede mudança na web (Decisão 4).

**c. Correspondência (como o conversor do Core já faz):**

| Web | Banco | Regra |
|---|---|---|
| Tipo | Tipo | igual; tipos de torno vão ao banco de torno; broca vai ao de fresa |
| T | T | igual; torno acima de 99 não entra, com aviso |
| Ø | Ø | Ø 10 (padrão do editor) entra **"a conferir"** |
| Raio de canto | Raio de canto | 0 entra ausente |
| Ângulo da ponta | Ângulo da ponta | 118 entra ausente |
| Comprimento de corte | Comprimento de corte | 25 entra ausente |
| Suporte Ø / comprimento | Suporte Ø / comprimento | 35 / 45 entram ausentes |
| Largura do inserto | Largura do inserto | só bedame; 3 entra ausente (a ferramenta fica "incompleta") |
| (não existe) | nome, arestas, comprimento total, ISO, raio de ponta, material, rpm, avanços | ausentes |

### Parte 3, exportar/importar

**a. Já existe** na tela Ferramentas (`Modules/ToolBankView.cs`), nunca conferido à mão pelo Igor:
- "Importar banco" (`:579`): escolhe um `.casillas-ferramentas`; entra como banco novo. Id ou nome repetido vira cópia
  ("(cópia)"), nada é sobrescrito (`ToolBankImport.Import`).
- "Exportar banco" (`:625`): copia o arquivo do banco selecionado (`ToolBankStore.Export`).

**Falta:** trazer ferramentas de um arquivo para **dentro** de um banco que já existe (com conflito de T à vista); exportar só
algumas ferramentas; conferência à mão dos dois seletores.

**d. Arquivo inválido, hoje:** "✕ Não importado: " + motivo ("Não é um arquivo de banco de ferramentas do Casillas.", "O arquivo
está vazio.", "…está estragado.", "Tipo de ferramenta desconhecido"). Versão mais nova: entra **só para leitura** com aviso; se o
nome ou o id já existem, recusa e pede para atualizar o Casillas Code.

## 2. Plano proposto e divisão em PRs

Cada PR sai do `dev`, pequeno, com o teste mestre (8 G-codes) verde antes e depois.

**D1. Torno no banco ativo (Core + Code + Trabalho do torno).**
- `ActiveLatheBankId` no `config.json`, ao lado do `ActiveMillBankId`. Sem escolha ou banco apagado: o `Primary`, igual a hoje.
- Code: Ø do torno, desenho da ferramenta do torno e folha de setup passam a ler o banco ativo do torno
  (`ToolBankLookup.Tools`, `LatheSetupTools`). O simulador por dentro não muda.
- Trabalho do torno: seletor "Banco ativo" e a escolha lendo o banco (só ativas). Escolher copia T, nome e rpm (se cadastrado),
  como hoje. O Enviar ao Code para de gravar na tabela antiga. G-code do torno e `.casillas-job` não mudam.
- Migração (uma vez, com marca própria): cada linha de torno da tabela antiga cujo T não está no banco de torno entra nele com
  "a conferir". T que já está no banco: vale o do banco e sai um aviso se nome ou tipo forem diferentes. Avisos no painel
  "Avisos da migração" do #171.
- Se falhar no meio: o banco é gravado em arquivo temporário e trocado no fim (`ToolBankStore.Save`), então o arquivo anterior
  não estraga. A marca só é gravada se o banco gravou; sem a marca, tenta de novo na próxima abertura. A tabela antiga não é
  apagada neste PR.
- Igor testa: abrir com o `config.json` de hoje e ver as ferramentas de torno no banco, com os avisos; escolher no Trabalho do
  torno; trocar o banco ativo e ver o Ø e o desenho no Code; programa de torno antigo simula igual.

**D2. Tirar a tabela antiga do `config.json`** (depende da Decisão 1 e da 2). Pequeno. Igor testa: Assist plano 2D dá o mesmo
resultado de antes; nada sumiu do banco.

**D3. Importar para um banco existente + conferência do que já existe.** Botão "Importar ferramentas para este banco": mostra a
lista do arquivo antes de gravar (nova, igual, T em conflito) e só grava no "Importar" (Decisão 5). Formato: o mesmo
`.casillas-ferramentas` versão 1, um banco e uma máquina por arquivo (fresa e torno separados). Arquivo de máquina diferente
do banco: recusa com o motivo. Versão mais nova: recusa pedindo atualizar. Igor testa: exportar, importar em outro banco,
conflito de T, arquivo estragado.

**D4. Importar da web** (só se a Decisão 4 autorizar mexer na web).

**D5. Ajustes do desenho (já aprovados), PR próprio e pequeno,** porque mexem no desenho da fresa e não no torno:
broca de centro com ângulo → cone e facear com raio → toroidal (`CamHandoffCode.ProfileOf`, poucas linhas e testes; macho,
alargador e mandrilhadora seguem topo reto); etiqueta "do CAM" no desenho (lugar na tela ainda não conferido em 1280). Pode
ir antes do D1. Muda a remoção de material desses T (é o objetivo).

**Pendências do B2 (não aprovadas), só o tamanho:**
- Ferramenta sem Ø é oferecida: pequeno (uma regra em `ToolBankPick.Choices` e um teste). Decidir se some da lista ou aparece desabilitada.
- Layout em 1280 (seletor tira ~170 px): médio. Precisa mudar o lugar do seletor e de foto nova; não levantei as opções de lugar.
- Operação nova vazia trava o Salvar: pequeno no código, mas mexe na regra do CAM-12 (campo vazio bloqueia Salvar). É decisão de regra, não de tamanho.

## 3. Decisões do Igor

1. **O Assist lê a tabela que o D quer tirar. Como ele continua igual?**
   a) A lista antiga fica guardada, parada, só para o Assist ler, até chegar a vez dele. Ninguém mais lê nem grava. **(recomendo)**
   b) O Assist já passa a ler o banco ativo da fresa agora. Muda o resultado do plano e adianta a unificação dele.
   c) Tirar a tabela e o Assist fica sem ferramentas (todo plano pede "ferramenta nova"). Não recomendo.
2. **Onde fica essa lista parada (se 1a)?**
   a) Continua no `config.json`, com outro nome, marcada "só Assist". **(recomendo: menos risco)**
   b) Vai para um arquivo separado na pasta do aplicativo.
3. **Trabalho do torno: quanto do B2 entra no D1?**
   a) Só o seletor do banco ativo e a escolha. **(recomendo: PR pequeno)**
   b) Tudo igual à fresa: operação nova com ferramenta vazia, aviso de T em conflito e "Adicionar ao banco ativo". Vira um PR a mais (D1b).
4. **Importar da web sem a conta única:**
   a) A web ganha um botão "Baixar minhas ferramentas" que gera arquivos de banco (um de fresa, um de torno), e o desktop os lê
      pelo "Importar" que já existe. Mudança na web: um botão no editor de setup (`setup-ui.js`) e a chamada ao conversor do Core.
      No desktop, nada novo. **(recomendo)**
   b) A web baixa a lista crua e o desktop ganha um "Importar da web". Mudança nos dois lados.
   c) Esperar a conta única (PLAT-03) e não fazer a parte 2 agora.
   Sem mudar a web não há caminho usável pelo cliente. As opções (a) e (b) **não estão autorizadas** por esta ficha.
5. **Importar para um banco que já tem aquele T:**
   a) Fica a do banco; a do arquivo aparece na lista como "não entrou", com um botão por ferramenta para substituir. **(recomendo)**
   b) A do arquivo entra no próximo T livre, com "a conferir".
   c) Perguntar ferramenta por ferramenta antes de gravar.
   Ferramenta igual: pula, sem aviso. Mesmo nome com outro T: entra e avisa.
6. **Exportar só algumas ferramentas?** a) Não agora: banco inteiro, como já é. **(recomendo)** b) Sim, com caixas de seleção.
7. **Ajustes do desenho (D5):** a) PR próprio, antes do D1. **(recomendo)** b) Junto com o D3.
8. **Pendências do B2:** quais entram (sem Ø / layout 1280 / Salvar travado), ou nenhuma agora.

## 4. Riscos e o que não foi conferido

- **Mesmo T na fresa e no torno:** `ToolBankLookup.Tools` junta os dois bancos e o da fresa vale por cima, mesmo em programa de
  torno. Já é assim; o D1 mantém. Separar por máquina do programa mudaria o Ø de quem tem T repetido: só com ordem do Igor.
- Linha de torno que foi para a tabela pelo Enviar ao Code tem Ø 10 de fábrica: na migração entra "a conferir".
- Linha da tabela sem tipo ou com tipo de fresa não é tratada pelo D1 (já foi ao banco de fresa no B1).
- A ordem aprovada (torno → 3D do CAM → Assist → setup do programa) fica intacta com a Decisão 1a.
- O `.casillas-cam` não muda no D (o torno não envia pacote: `LatheJobView` não usa `CamHandoff`). Segue `versao: 1`.
- **Não conferido:** nada rodado nem testado (só leitura de código); os seletores de Importar/Exportar à mão; onde cabe a etiqueta
  "do CAM" no desenho; opções de lugar do seletor em 1280; `config.json` e bancos reais do Igor; publicação da web; máquina.
