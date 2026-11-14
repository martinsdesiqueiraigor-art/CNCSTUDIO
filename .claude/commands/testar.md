---
description: Testa a missão. Use "/testar rapido" no meio do trabalho e "/testar completo" no fechamento.
---
Modo pedido: $ARGUMENTS (se vazio, use "rapido").

**rapido**
1. Compile uma vez só o que mudou.
2. Rode só os testes da área alterada (filtro pelo projeto ou pela classe de teste, ex.: `dotnet test tests/CncStudio.Core.Tests --filter "FullyQualifiedName~Classe" -v q`). Diga qual filtro usou e por quê.
3. Saída resumida: só o total e as falhas. Não despeje a lista dos testes que passaram.

**completo**
1. Compile a solução inteira (`dotnet build -c Release`).
2. Rode a suíte inteira (`dotnet test tests/CncStudio.Core.Tests -v q`), com saída resumida. O teste mestre (8 G-codes de referência, classe `CamJobGoldenTests`) precisa estar verde.
3. Só abra o aplicativo se o Igor escreveu "abrir".

**Se algo falhar**
- Falha em código desta missão: corrija dentro do escopo da ficha e repita.
- Falha fora do escopo da ficha: PARE e relate o erro e a hipótese.
- Nunca altere um teste existente nem os G-codes de referência para fazer passar.

Termine com no máximo 3 linhas: o que rodou, o resultado, o que falta.
