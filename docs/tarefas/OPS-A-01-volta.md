# OPS-A-01: operações A (bolsão com opções, macho, mandrilhar)
Nível G · Base `dev` em `6c22848` · Ramo `ajuste-OPS-A-01`

## O que mudou
- **Bolsão, entrada** (só quando escolhida): rampa com ângulo (dá voltas inteiras para não passar do ângulo; avisa se passar de 3 voltas), hélice com Ø e passo digitados (avisa e desce em rampa se não couber), mergulho reto, pré-furo (usa o primeiro furo de uma Furação anterior que cai dentro do bolsão; sem furo, avisa e mergulha reto). Ø da hélice precisa ser maior que o da ferramenta.
- **Bolsão, estratégia**: anéis de fora para dentro (como antes), zigue-zague, anéis de dentro para fora e espiral. De dentro para fora e espiral exigem uma entrada escolhida (sem entrada o programa não gera). Com ilhas só vale a limpeza por passes retos; a estratégia escolhida é ignorada com aviso.
- **Furação**: macho G84 (com `M29 S` antes, avanço = o da ferramenta), mandrilhar G85 (saída em avanço; com pausa digitada vira G89) e G86 (fuso parado). **Só Fanuc**: em Siemens o Trabalho recusa com mensagem clara (não existe simulador de CYCLE84/85/86 aqui; preferi recusar a gerar algo que não dá para conferir).
- **Arquivo `.casillas-job` versão 7**, só quando o trabalho usa entrada/estratégia do bolsão ou macho/mandrilhar. Sem isso sai versão 1–6 como antes; trabalho antigo abre e gera igual. App antigo recusa o novo ("versão mais nova").
- Tela do Trabalho: campos Entrada, Estratégia, Ângulo da rampa, Ø e passo da hélice; Ciclo ganha G84, G85, G86 (marcados Fanuc).

## Não feito (fica para ficha própria)
- **Chanfro** (A3): nova operação mexe em paleta, arquivo, tela e 3D; prefiro ficha separada (OPS-A-02) a entregar às pressas.
- Fresar resto, criação pelo 3D/Assist e torno (fora do escopo da ficha).

## Testes
Novos: `CamPocketOptionsTests` (18) e `CamTapBoreTests` (9). Core: 2072 verdes; os 8 G-codes de referência intactos; build Release sem avisos.
**Testes existentes alterados (autorizado pelo Igor para o primeiro; os outros três são a mesma troca, avisar):** o exemplo de "versão futura" passou de 7 para 8 porque 7 agora é versão válida. `CamJobStockShapeTests` (autorizado), `CamJobTests` linha 205, `CamJobProgramTests` linha 210, `CamJobZeroTests` linha 342 (mesma troca; o Igor autorizou só o primeiro de forma expressa, os três outros ficam para ele conferir).

## Não testado
- No .exe (tela nova do bolsão e do ciclo de furação).
- G84 com M29 em máquina real: o comando de macho rígido varia por controle (M29 é o padrão Fanuc); conferir no pós da máquina.

## Roteiro (.exe)
1. Bolsão: trocar Entrada para Rampa 3°, Hélice (Ø16, passo 1), Mergulho e Pré-furo (com uma Furação antes); simular cada uma.
2. Estratégia Zigue-zague, De dentro para fora (sem entrada deve avisar erro) e Espiral.
3. Furação com G84, G85, G86: ver o código e a simulação; trocar o dialeto para Siemens deve recusar.
4. Salvar e abrir o arquivo; trabalho antigo abre e gera igual.
