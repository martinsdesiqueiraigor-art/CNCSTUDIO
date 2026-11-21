# CAM-ILHAS-01: o bolsão do 3D reconhece os limites da face (Volta)
Ramo: `ajuste-CAM-ILHAS-01` (base `origin/dev` em `d697084`). Tamanho M. **Só Core.** Não mexi no App, no torno, na Web nem em outros geradores. Os 8 G-codes de referência ficaram intactos.

## O que entrou
- **F1 (`MeshFeatures.ClassifiedLoops`)**
  - Cada laço da face de fundo ganha um lado, decidido pela região vizinha de cada aresta:
    - toda acima do fundo: **sobe**;
    - toda abaixo: **desce**;
    - raio ou chanfro que passa dos dois lados: decide o vértice do triângulo vizinho.
  - Um laço com os dois lados é **misto**.
  - `BoundaryLoops` passou a usar o mesmo código e devolve os mesmos pontos de antes (há teste disso).
- **F2: filtro novo `LoopFilter.Auto`**, padrão do bolsão criado a partir do 3D:
  - Laço de dentro que sobe vira ilha. O que desce (furo, escareado) fica de fora.
  - Laço de fora que sobe dá bolsão fechado, como antes.
  - Laço de fora que desce é **borda aberta**: o limite vira o corte do bruto na altura do fundo, aumentado do raio da fresa mais 1 mm.
    - Bloco: retângulo.
    - Cilindro ou tubo em pé: círculo externo.
    - Cilindro ou tubo deitado: retângulo da corda.
  - **Mensagens de erro (sem G-code dessa operação):**
    - Borda aberta sem bruto, com bruto STEP ou com o fundo fora do bruto: "Borda aberta precisa de bruto bloco, cilindro ou tubo."
    - Laço misto: "Limite misto (parte sobe, parte desce): ainda não suportado; escolha Todos ou Só o de fora."
  - No automático, as ilhas são só os contornos da própria operação. Contornos de outras operações dentro do bolsão não viram ilha.
  - "Ilha perto demais da parede" só vale contra parede de verdade, porque na borda aberta a parede é o bruto.
- **Arquivo:**
  - Versão 8 só quando algum bolsão usa o automático, com a chave nova `"border"` na geometria.
  - Sem o automático, o texto do arquivo sai igual ao de antes.
  - Arquivo antigo abre e gera igual.

## Correção achada no teste do .exe (21/11, 04:00, liberada pelo Igor)
- **Defeito (já existia no bolsão com ilhas):** ao trocar de passe, a fresa subia e andava num rápido só (ex.: `G0 X31.973 Z2.` saindo de Z−16) e cruzava a ilha na diagonal. No .exe do PR, a simulação mostrou entalhes no cubo da BOLTED_FLANGE. Na máquina seria colisão. O "Todos" fazia o mesmo.
- **Correção (`PocketIslands.cs`):** todo rápido que sai do fundo para outro ponto agora sobe reto antes de andar. Só muda o G-code de bolsão com ilhas; nenhum dos 8 de referência tem ilha.
- **Teste novo:** `Rapido_sobe_reto_antes_de_andar_para_nao_cruzar_a_ilha` (falhava antes da correção: rápido de Z−2,5 a Z2 andando).

## Decisões técnicas da execução
- **Na dúvida, sobe.** Laço sem vizinha conhecida (malha aberta, ou vértice no meio da aresta) conta como subindo, que é o lado seguro: vira parede ou ilha e nunca é cortado.
- **Misto e sem bruto saem como erro com a mensagem, não como "Incompleta".** Motivo: "Incompleta" hoje é só campo vazio ou inválido, e um campo falso daria um texto errado ("Falta …").
- **Tela:** o seletor Laços ainda só tem "Todos" e "Só o de fora". Num bolsão novo do 3D, nenhum dos dois aparece marcado. O botão "Automático" e o resumo ("1 ilha, 8 furos ignorados, borda aberta") ficam para a CAM-ILHAS-02. Escolher "Todos" ou "Só o de fora" continua funcionando como antes.

## Testes existentes alterados (liberado pelo Igor em 21/11)
- **4 testes usavam "versão 8" como exemplo de arquivo mais novo** e agora usam a 9, porque a 8 passou a existir. A regra testada não mudou. São eles:
  - `CamJobTests.Versao_mais_nova_ou_dado_estranho_sao_recusados`;
  - `CamJobStockShapeTests.Leitor_recusa_versao_maior_que_a_atual`;
  - `CamJobProgramTests.Leitor_ate_v2_exige_os_dois_campos_e_v3_aceita_sem_eles`;
  - `CamJobZeroTests.Leitor_recusa_versao_maior_e_abre_v3_com_e_sem_zero`.
- **2 testes do bolsão do 3D** agora esperam o filtro automático e a versão 8, no lugar de Todos e da versão 6. O resto deles (ilha, G-code sem entrar no pino, volta igual) segue igual:
  - `StepOpsGeometryTests.Bolsao_da_selecao_guarda_a_geometria_e_a_ilha_entra_sozinha`;
  - `StepOpsGeometryTests.Geometria_vai_para_a_versao_6_e_volta_igual`.

## Testes
- Novos:
  - `LoopSideTests` (7): flange (cubo sobe, furo e borda descem), furo escareado, bolsão fechado, ilha com chanfro na base, degrau misto, mesmos pontos que `BoundaryLoops` e laço sem vizinha.
  - `CamIslandsAutoTests` (7):
    - flange com bruto bloco: o cubo vira ilha e o furo fica de fora; a fresa vai além da peça e não passa do bruto mais o raio mais a folga;
    - sem bruto, a mensagem de erro;
    - misto, a mensagem de erro;
    - bolsão fechado com pino e furo no fundo;
    - versão 8 volta igual e gera o mesmo programa;
    - filtro antigo sem "border" e sem versão 8;
    - corte do bruto bloco.
- Core: suíte inteira, **2118 verdes** (depois da correção do rápido), com `CamJobGoldenTests` (8 G-codes intactos).
- App: build Release, **0 avisos e 0 erros**.

## O Igor testa no .exe
1. Abrir a BOLTED_FLANGE com bruto cilindro e criar um **Bolsão novo** clicando na face do topo da flange (a que tem os furos).
2. Antes, remover o bolsão antigo (operação 2 do BOLTED_FLANGEigor), que usina tudo até Z−16. Usar o .exe do PR 214 (o da pasta Downloads é o antigo, do PR 213).
3. Gerar: o cubo do meio fica de pé, sem entalhes na simulação, a fresa vai até fora da peça, e os furos dos parafusos não dão erro de "ilha perto demais".
4. Um bolsão antigo (salvo antes, com Todos ou Só o de fora) gera igual a antes.

## Sem teste
- Malha real de STEP no .exe (só malhas sintéticas).
- Cilindro e tubo no teste automático: o código existe, mas só o bloco tem teste.
- Nada em máquina.

## Só com ordem do Igor
- Merge no `dev`.
- CAM-ILHAS-02: tela com o "Automático", o resumo e a peça transparente sobre o bruto. Imagem antes.
- Entrada pela borda aberta (rampa de fora), dividir laço misto e furar sozinho os furos ignorados.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01B7m9Dy6bYdERgVsF6gHmbH
