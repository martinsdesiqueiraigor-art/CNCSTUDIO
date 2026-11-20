# FECHO-02: fecho da interface e versão 1.2.0 (Volta)
Ramo: `ajuste-FECHO-02` (base `origin/dev` em `592315b`). Tamanho M. Só App, versão e documentos. Core, G-code, arquivos e testes: intocados.

## O que entrou (um commit por fatia)
1. **Conferência em tela virtual (mínima, a pedido do Igor).** CAM do torno a 150% numa janela de 1280 lógicos: a faixa (menu "Operações ▾", Fixação, Banco ativo), a árvore, o 3D, a linha do tempo, o console "Avisos · nenhum", a biblioteca recolhida e a barra de pendências cabem sem cortar nem rolar. Sem correção de código nesta fatia.
2. **Chips Iso, Topo e Frente saem** da barra do 3D da fresa (`MillJobSceneView`, mais o método `ViewButton` que ficou sem uso). O cubo de vistas faz o mesmo.
3. **Versão 1.2.0** no `CncStudio.App.csproj` e em `installer/CncStudio.iss`; nota em `docs/versoes/1.2.0.md`.
4. **`docs/ESTADO.md` em dia**, com o limite novo registrado: o CAM ainda não acompanha o material (bruto atualizado a cada operação).

## Testes
- Build do App em Release: 0 avisos, 0 erros.
- Teste mestre `CamJobGoldenTests`: 8 de 8. A suíte completa do Core roda na Prévia do PR (nada do Core mudou).
- Nenhum teste existente alterado.

## O que o Igor confere no .exe
1. O rodapé ou Sobre mostra 1.2.0.
2. A barra do 3D da fresa sem Iso, Topo e Frente; o cubo de vistas continua funcionando.
3. Um trabalho antigo de fresa e um de torno abrem e geram igual.

## Sem teste
- 125%; biblioteca com T e Ø preenchidos; fresa a 150%; torno no .exe; as abas Rosca, Furação axial, Canais e interno do torno.
- Nada em máquina.

## Só com ordem do Igor (não feito)
Merge deste PR e dos anteriores no `dev` já feito; `dev` → `main` (publica a web); tags `v1.1.0` (commit `7db0be1`) e `v1.2.0`; apagar os ramos `ajuste-*` mesclados (20, mais este).
