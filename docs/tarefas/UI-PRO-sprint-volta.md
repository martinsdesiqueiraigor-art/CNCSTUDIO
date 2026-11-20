# UI-PRO sprint: P3a, P3b e P4 (menu Operações, árvore com Geometria, cubo de vistas, biblioteca de ferramentas, acabamento)
Modo sprint (Igor, 19/11) · Base: ramo da P2 (`ajuste-UI-PRO-P2`, PR #207, commit `a22eb82`, sobre o `dev` `d3123f9`) · Ramo `ajuste-UI-PRO-sprint`
**Este PR leva junto o commit da P2.** Se a P2 for mesclada antes, ele fica só com os 5 commits novos.

## O que mudou (só `src/CncStudio.App`; um commit por fatia)
1. **P3a-1, menu Operações.** A faixa de operações (2D, Furação, 4º eixo; no torno, Torneamento, Canal e corte, Rosca) virou um botão "Operações ▾" com um menu por grupos (mesmas operações, mesmos nomes, "em breve" apagado). Arquivo, Fixação e Banco ativo ficam na faixa. Os botões do menu entram na mesma lista que a tela já apagava conforme o estado (dica "Agora não: …" igual). Criar operação = dois cliques. Código: `CamShell.BuildPalette(opsMenu: true)`.
2. **P3a-2, árvore expansível com Geometria.** A operação feita no 3D ganha o nó "Geometria (N)" com uma linha por face ou aresta (só leitura; linha não achada no STEP mostra o motivo). Passar o mouse numa linha acende no 3D só aquela parte (ou tudo, no nó); sair apaga. Clicar numa linha volta a seleção para a operação. O nó lembra se estava aberto. Reaproveita o destaque que já existia (`SetPart`); a cena 3D não foi tocada.
3. **P3b-1, cubo de vistas.** Cubo isométrico no canto do 3D com Topo, Frente e Lado clicáveis e botão Iso; chama `Fit(SceneView)` das cenas que já existem. Aparece só com o 3D ligado, na fresa e no torno.
4. **P3b-2, biblioteca de ferramentas.** Tira recolhível à direita do 3D (`ToolLibrary.cs`): aberta (190 px), mostra T, Ø e nome das ferramentas das operações (uma vez cada T+Ø; torno: T e nome, Ø "—"; "?" onde o campo está vazio). Recolhida: tira de 30 px com a contagem. Nasce recolhida; a escolha vale enquanto o app está aberto. Em 1280, aberta, o 3D fica com ~70% da largura do centro.
5. **P4, acabamento.** Contraste: o cinza "apagado" (`Faint`, texto de botão desligado e olho fechado) passou de 3,2:1 para 4,8:1 no cartão; itens "em breve" e "Material: não definido" passaram de opacidade 0,5 (4,4:1) para 0,6 (5,8:1). As demais cores do CAM já passavam de 4,5:1 (medi). Altura dos campos: já era a mesma na fresa e no torno (26). Procurei texto em inglês no CAM e não achei.

## Testes
Core não mudou: 2087 verdes (inclui os 8 G-codes). Nenhum teste alterado. Build Release do App sem avisos.

## Conferido na tela (App no Linux, tela virtual, 1280 de largura)
Menu Operações aberto na fresa e no torno; cubo de vistas com o 3D ligado (clicar nas faces não deu erro); biblioteca recolhida e aberta (sem ferramentas: texto de ajuda), 3D com ~70%.

## Não conferido (o Igor testa no .exe)
- **Geometria e destaque ao passar o mouse:** precisa de STEP (OpenCASCADE só no Windows).
- Biblioteca com ferramentas de verdade (a lista é montada igual à do nó Ferramentas da árvore); clicar nas faces do cubo com uma cena desenhada.
- Criar operação pelo menu com contornos e ver a dica "Agora não"; 125% e 150%; Windows.

## Para o Igor testar no .exe
1. Fresa e torno: Operações ▾ → criar cada operação em dois cliques; conferir que nada sumiu.
2. Abrir um STEP, criar operação pela face: abrir "Geometria" na árvore e passar o mouse nas linhas (acende no 3D).
3. Ligar o 3D: clicar Topo, Frente, Lado e Iso no cubo.
4. Abrir a biblioteca (‹ à direita do 3D): T, Ø e nome; recolher de novo.
5. Ler o texto apagado ("em breve", botões desligados): legível.

## Decisões e avisos
- Os botões Iso, Topo e Frente da barra de chips do 3D continuam (estão na cena, que a ficha proíbe tocar): ficam duplicados com o cubo. Retirar os da barra é uma linha na `MillJobSceneView`, só com ordem.
- "Gerar G-code" em destaque no topo (ficha P3 antiga) não foi movido: já fica sempre visível na barra de baixo, com o motivo quando travado.
- Biblioteca é só leitura: nome vem do campo da operação; o banco não é consultado.
- `Faint` também é a cor do texto de botão desligado: botão desligado ficou um pouco mais claro.
