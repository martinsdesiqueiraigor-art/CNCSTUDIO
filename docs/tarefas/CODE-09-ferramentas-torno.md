# CODE-09 — Ferramentas de torno

**Objetivo (Igor):** melhorar a imagem da ferramenta de torno e ter tipos selecionáveis por T, cada um com desenho e corte próprios: desbaste externo, barra interna, bedame externo e interno, rosca externa e interna, broca.

## Entrega 1 (este ramo, `ajuste-torno-ferramentas`)
- `ToolKind` ganha `torno-externa`, `torno-interna`, `bedame`, `bedame-interno`, `rosca-externa`, `rosca-interna`; `SetupTool.InsertWidth` (largura do bedame, padrão 3 mm). A broca (`broca`) continua a mesma.
- `Core/Scene/LatheToolShape`: uma só fonte do desenho (inserto + corpo) e da largura de corte por tipo. Usada no 3D (`SceneMeshes.LatheTool`) e no 2D do desktop (`GraphicsView.DrawLatheTool`).
- Corte: o bedame remove a largura inteira do inserto a partir do canto da ponta (para +Z); rosca corta fino (0,4 mm); desbaste e barra interna como antes (raio de ponta 0,4).
- Web: editor de ferramentas do setup com os 6 tipos + largura do inserto.
- Sem ferramenta cadastrada: vale desbaste externo (igual a antes, com o inserto melhor desenhado).

## Fica para depois
- Editor de tipo no desktop (hoje só a tabela de diâmetros; o tipo vem do setup/JSON).
- Perfil real da rosca (G76/G92) e passo no material; chanfros/raio do bedame.

## Volta
Ver `docs/tarefas/MODELO.md`.
