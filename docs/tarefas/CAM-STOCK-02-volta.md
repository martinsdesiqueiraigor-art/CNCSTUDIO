# CAM-STOCK-02: tela do bruto depois de cada operação (Volta)
Ramo: `ajuste-CAM-STOCK-02` (base `origin/dev` em `5784336`). Tamanho M. **Só App**: Core, G-code, os 8 de referência, o formato do `.casillas-job` e o torno ficaram intactos.

## O que entrou (um commit)
- **Cálculo fora da tela** (`JobView.Stock.cs`, novo):
  - a cada mudança no trabalho, chama `CamStockTimeline.Compute` numa cópia do trabalho;
  - espera 250 ms se a pessoa ainda estiver digitando;
  - cancela o cálculo anterior;
  - passa o anterior em `previous`, então recalcula só da operação mudada em diante.
- **Selo na árvore** (2ª linha de cada operação, à direita do tempo):
  - "calculando…" em azul;
  - "bruto ✓" em verde;
  - "sem estado" em amarelo, quando é a própria operação;
  - "sem estado" em cinza, quando a culpa é de uma anterior.
  - O motivo vai na dica. Operação desligada fica sem selo.
- **3D** (`MillJobSceneView`):
  - com uma operação escolhida, o bloco inteiro dá lugar à malha do material depois dela, deslocada pelo zero (`Offset`);
  - a malha é montada fora da tela;
  - os chips de antes (Bruto, Peça, Arestas, Trajetória, Rápidos, Eixos) continuam;
  - sem estado, chave desligada ou nada escolhido: o 3D de antes.
- **Barra sob o 3D:** Bruto inteiro, Antes da operação e Depois da operação (padrão). Só aparece com o 3D ligado e uma operação escolhida.
- **Propriedades:** seção "Bruto depois desta operação" no fim do painel da operação, só leitura:
  - estado;
  - material tirado e sobra (cm³);
  - motor;
  - tempo do cálculo;
  - botões Ver antes e Ver depois;
  - nota "verificação, nunca garantia".
- **Faixa:** chave "Bruto por operação: Ligado/Desligado", à esquerda do banco ativo. Fica em `AppSettings.CamStockSteps` (nasce ligada), nunca no trabalho. Desligada, nada é calculado.
- **Console e rodapé:**
  - linha "Conferir" fixa com o aviso de verificação;
  - no rodapé, "Bruto por operação: N calculados, K sem estado" (ou "calculando…", ou o motivo, como "Bruto não definido").

## Decisões técnicas da execução
- **Linha do tempo:** continua com as cores de hoje (Pronta, Incompleta, Erro). Colorir pelo estado do bruto brigaria com a legenda. Clicar num bloco já escolhe a operação, e o 3D acompanha.
- **Peça e Trajetória:** não ganharam botões novos sob o 3D, porque os chips de cima já fazem isso. Duplicar confundiria.
- **"Bruto inteiro" e "Antes" da primeira operação mostram o sólido do bruto que o 3D já desenhava.** Motivo: achei um defeito no Core da CAM-STOCK-01 (`ShowAfter(-1)` devolve o bruto final, porque o instantâneo inicial é tirado depois da simulação). A ficha proíbe mexer no Core, então contornei no App. **Proposta:** ficha P `CAM-STOCK-01b` para guardar o instantâneo antes de simular, com um teste de `ShowAfter(-1)`.
- **Um commit só, não dois:** o cálculo, os selos e o 3D ficaram entrelaçados no mesmo arquivo novo.

## Testes
- App: build Release, **0 avisos e 0 erros**.
- Core: suíte uma vez no portão, **2103 verdes**, com `CamJobGoldenTests` (8 G-codes intactos). Nenhum teste mudou.
- Conferido em tela virtual (Linux/Xvfb):
  - **1280:** selos "bruto ✓" nas 5 operações, chave na faixa, aviso no console, rodapé "5 calculados", seção nas propriedades (Contorno: tirou 4,3 cm³, sobra 49,0 cm³), barra sob o 3D sem cortar;
  - **150%:** trabalho com a operação 3 sem diâmetro. Selos 1 e 2 "bruto ✓", 3 "sem estado" (amarelo), 4 e 5 "sem estado" (cinza), rodapé "2 calculados, 3 sem estado". O bruto é calculado mesmo com o G-code travado. Barra e seção cabem.
  - **Chave desligada:** selos, seção e barra somem; o 3D é o de antes.
- **3D não conferido na tela virtual:** o OpenGL não desenha nada no Xvfb deste ambiente (nem o bloco de antes). Conferi por código que a malha do material cai na posição do bruto, com o zero no canto e no meio (`fresa-zero-g55-canto` e `fresa-bloco-siemens`). Foi um teste temporário, apagado depois.
- Tempo: os selos já estavam prontos no primeiro print, cerca de 5 s depois de abrir o trabalho. O cálculo em si, medido na CAM-STOCK-01, é de cerca de 2 s para 20 operações.

## O Igor testa no .exe
1. Abrir um trabalho de fresa com bloco: os selos passam de "calculando…" a "bruto ✓".
2. Ligar o 3D e clicar em Faceamento, depois em Contorno: topo plano, depois o degrau. Testar Antes, Depois e Bruto inteiro.
3. Apagar um campo de uma operação do meio: ela fica "sem estado" (amarelo) e as seguintes também (cinza). Desligar a chave: volta o 3D de antes.

## Sem teste
- O 3D no Windows (só por código).
- Peça STEP junto com o material.
- Voxels na tela (3+2 e texto no cilindro).
- 125%.
- Trabalho grande real.
- Nada em máquina.

## Só com ordem do Igor
Merge no `dev`; ficha `CAM-STOCK-01b` (correção do `ShowAfter(-1)` no Core).
