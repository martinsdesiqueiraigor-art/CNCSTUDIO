# Volta UX-04h (PR D5), 14/11/2026

Estado do conteúdo técnico: rascunho (só o Igor marca revisado).

```
ID: UX-04h D5 (ajustes do desenho da fresa)
O que foi feito:
  1. Broca de centro com ângulo da ponta → cone (desenho e corte do simulador).
  2. Facear com raio de canto → toroidal (desenho e corte).
  3. Etiqueta "do CAM" na linha "Ferr." abaixo do gráfico (mesma regra e texto das Estatísticas: T no pacote e o setup do programa
     não o redefine). Foto 1280×720: docs/tarefas/UX-04h-D5/UX-04h-D5-01-etiqueta-do-cam-1280x720.png ("Ferr. T42 Ø6 · do CAM").
  Macho, alargador e mandrilhadora seguem topo reto. Torno, G-code, .casillas-job e .casillas-cam (versão 1) não mudam.
Arquivos: Core Cam/CamHandoffCode.cs (ProfileOf, 2 linhas); App Graphics/GraphicsPanel.axaml.cs (FromCam, 1 linha de texto),
  MainWindow.axaml.cs (liga FromCam a ToolFromCam); testes CamHandoffProfileTests.cs; docs/ESTADO.md; esta Volta e a foto.
Testes: dotnet test local: 1865 passaram, 0 falharam. Teste mestre (CamJobGoldenTests) 8 de 8 antes e depois. Build Release 0 avisos.
  Novos: perfis cone (broca de centro) e toroidal (facear), valores fora da faixa e sem campo caem no topo reto; remoção de material
  muda só no T com os campos e fica igual sem eles.
O que mudou / o que ficou igual na remoção de material:
  Muda: T do pacote tipo "broca-centro" com ângulo (0<a<180) e tipo "facear" com raio de canto (0≤raio≤Ø/2). A canaleta/furo deles deixa
  de ter fundo reto: sobra material no canto (toroidal) e a ponta é cônica.
  Igual: esférica, toroidal e broca como no C2; macho, alargador, mandrilhadora, topo; qualquer T sem os campos; programa sem pacote;
  T que está no setup do programa (o setup vale por cima).
O que o Igor testa (passo a passo):
  1. No CAM, banco ativo com uma fresa de facear com raio de canto e uma broca de centro com ângulo da ponta. Trabalho com
     Faceamento (T da facear) e Furação (T da broca de centro). "Enviar ao Code".
  2. No Code, rodar a simulação até o fim. Conferir: cantos do passe do facear arredondados; furo da broca de centro com fundo em cone.
  3. Olhar a linha "Ferr." abaixo do gráfico: "T.. Ø.. · do CAM".
  4. Repetir com um T sem tipo ou sem raio/ângulo: igual ao de antes.
Riscos e o que não foi conferido:
  - Nada foi testado em máquina nem no .exe do Igor. A foto é headless (MainWindow real, programa de teste com T41/T42, pacote criado por
    script fora do repositório); a ponta desenhada é pequena no zoom padrão, o que se vê é a etiqueta.
  - Facear com raio grande vira toroidal fino; a simulação segue 3 eixos, grade de alturas, sem promessa de colisão.
  - A etiqueta usa a regra das Estatísticas: aparece em qualquer T do pacote, tenha ou não campos de perfil (decisão de manter igual).
  - O App gravou o config.json ao fechar na foto; restaurei a cópia e conferi que ficou idêntica.
Pendências: B2 (ferramenta sem Ø, layout 1280, Salvar travado) fora, sem decisão do Igor. D1 (torno) não começou.
```
