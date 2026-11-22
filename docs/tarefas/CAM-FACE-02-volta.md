# CAM-FACE-02 — Volta (contorno pela parede no menu da face)

## O que foi feito
- **Menu da face no Trabalho fresa** (como os CAMs de mercado), sempre com o que a face clicada pede:
  - **Fundo plano:** Faceamento, Bolsão e **Desbaste adaptativo** (o Bolsão convertido por `CamPalette.AsAdaptive`, do #223).
  - **Parede de furo:** Furação e **Contorno por dentro**.
  - **Parede de pino e parede reta:** **Contorno por fora** e **por dentro**. O lado errado fica apagado com o motivo ("o material fica dentro desta parede…").
  - **Parede inclinada:** **Chanfro**, com largura e ângulo da ponta lidos da face, e **Contorno pela base**, em degraus. Este último avisa que deixa a escada de material (Conferir no peça × bruto).
  - Cabeçalho novo: "Parede de pino Ø 44,0 mm · Z 0,0 → −16,0", "Parede reta · …", "Parede inclinada · … · ≈45° da vertical". O texto "Nada sai desta face ainda" saiu.
- **Core** (`Cam3D/WallSection.cs`, novo):
  - Corta uma seção horizontal da malha na parede: no meio da altura para parede reta, junto da base para inclinada.
  - Fica com o laço que passa pela parede. O lado vem da normal da parede (para fora do material).
  - Pino e furo completos viram círculo exato.
  - Parede voltada para baixo (contrassaída) não serve, com o motivo.
- **Alturas da operação:** topo = alto da parede, fundo = base da parede, como no fundo do bolsão do 3D. A mensagem pede para conferir. Ferramenta, passe e avanços nascem vazios. No chanfro, profundidade e entrada ficam vazias.
- `JobFromGeometry`: contorno e chanfro saem de uma parede pela mesma rota da seleção (também "Trocar seleção" e reabrir o arquivo). Nada novo no `.casillas-job`. O G-code das operações que já existiam não muda.
- `MeshFeatures` ganhou só a leitura `Mesh` (a malha, para a seção).

## Diferenças da ficha
- `MeshFeatures.cs` não estava em "pode mexer": entrou só a propriedade `Mesh`, de leitura.
- "Contorno da parede" e "Contorno pela base" são os nomes da operação na árvore.

## Testes
- Novo `WallFaceTests` (5):
  - pino Ø44 (círculo, por fora, Z0 → Z−16, menu e lado errado apagado);
  - parede reta do cubo por fora e furo por dentro;
  - contorno criado da parede gera por fora do cubo;
  - furo escareado: chanfro de 1 mm a 90° e contorno pela base;
  - reabrir o arquivo acha a parede.
- **Nenhum teste existente alterado.** Core: **2189 verdes**, 8 G-codes intactos. A solução compila sem avisos.

## O que o Igor confere no .exe
1. BOLTED_FLANGE: clique na parede do pino Ø44 → "Contorno por fora" → operação com Z0 → Z−16, sem ferramenta.
2. Parede do furo Ø24 → Furação e Contorno por dentro. Fundo Z−16 → Faceamento, Bolsão e Desbaste adaptativo.
3. Com ferramenta e avanços, gera e simula no Code sem erro.
