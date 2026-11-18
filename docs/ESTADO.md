# Estado atual

Atualizado em 18/11/2026 (FECHO-01, versão 1.1.0). Vale para o `dev` depois do fecho e para a `main` quando o `dev` entrar nela (só com ordem do Igor).
Histórico: `docs/historico/ESTADO-ate-2026-11-14.md` (o resumo anterior) e `docs/historico/ESTADO-ate-2026-11-02.md`. Ler só se a tarefa pedir.
Nota da versão: `docs/versoes/1.1.0.md`.

## O que existe
- **Code (web e desktop):** editor, simulação 2D/3D, comparação, painel de controle, Macro e Otimizador (web e desktop), Academy só na web (conteúdo em rascunho).
- **Plataforma web:** tela inicial, conta única com teste de 30 dias e travas por módulo, domínio `casillas.com.br`. O merge na `main` publica a web na hora.
- **CAM desktop, fresa:** Trabalho com faixa de ícones, árvore e propriedades; Preparação (Máquina, Peça pronta em DXF ou STEP, Bruto por sobra, cilindro, tubo ou STEP, Programa nº, G5x, Zero de 9 pontos); banco de ferramentas ativo; 4º eixo e 3+2 na mesa dupla; pacote `.casillas-cam` para o Code.
- **Modelo 3D no Trabalho (M3D, #186):** peça STEP no Trabalho (caminho, resumo e giros; nunca lida ao abrir); no 3D, fundo plano vira Bolsão e furos verticais viram uma Furação, na árvore, vazios até digitar. A aba Modelo 3D fica com varredura e desbaste 3D.
- **CAM desktop, torno (#184, #185):** Preparação (Máquina, Programa nº, G5x, refrigeração, bruto), operação nova vazia, faceamento, torneamento externo e interno, furação, rosca, canal radial, canal de face, sangramento, fixação (castanha dura e mole, contraponto, luneta), Z0 na face pronta ou oposta, pacote `.casillas-cam` do torno.
- **Assist (só desktop):** bibliotecas, orçamento, proposta, plano 2D.
- **STEP:** OpenCASCADE 7.9.3 (`CncStudio.Step`, só desktop).

## Arquivos
- `.casillas-job` da fresa: versões 1 a 3 como antes; versão 4 só com peça STEP ou furação com lista de furos.
- `.casillas-job` do torno: versão 1; chaves novas (Programa nº, bruto, Z0, fixação nova) só quando usadas.
- Teste mestre: `CamJobGoldenTests`, 8 programas de referência, iguais desde o início.

## Limites que valem hoje
- Nada do CAM rodou em máquina real. G-code novo do torno (canal de face, sangramento, Z da face oposta) nunca rodado.
- Fixação, queda da peça e colisão não são verificadas; simular e rodar a primeira vez no ar.
- Contorno lido da malha do 3D é poligonal (arcos viram trechos retos).
- STEP muito grande, IGES e peça real do Igor: não testados. `CncStudio.Step.Tests` fora da solução e do CI.
- O `.exe` e o instalador oficiais não estão publicados. Mínimo de tela: 1280×720.
- Siemens: o simulador trata G58/G59 como zeros 5 e 6 (`MachineExecutor.cs:630`).

## Fora desta versão
2ª fixação do torno; faixa da fresa em 1280 (rola); armário de ferramentas, D2, D3 e importar da web; operações novas de fresa (chanfro, ranhura, macho, mandrilhar); faceamento e contorno pela face do 3D; bolsão com ilhas pelo 3D; FreeCAD (estudo parado).

## Para vender o `.exe` (pendente)
Itens "a confirmar" do inventário de licenças (texto do aviso do OCCT, DLLs do OcctNet, textos de zlib, libpng, bzip2, brotli, FreeType, Visual C++, MicroCom, Tmds.DBus) e uma peça real em STEP testada.

## Ponteiros
- Fichas e relatórios: `docs/tarefas/` (modelo `docs/tarefas/_MODELO.md`; relatório de cada missão em `docs/tarefas/<ID>-volta.md`).
- Planos do torno: `docs/tarefas/TORNO-01-plano.md`. Relatórios desta versão: `TORNO-T1-T7-volta.md`, `M3D-volta.md`, `FECHO-01-volta.md`.
- Testes: vale o resultado do CI.
