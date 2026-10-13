# Etapa 3 — Casillas Code como produto comercial

Decisões tomadas com o dono do produto:

| Tema | Decisão |
|---|---|
| Nome | **Casillas Code** — "EDITOR E SIMULADOR CNC" |
| Marca | Base da logo A do Casillas + linhas de código (variação 05). Fontes em `branding/` |
| Versões | **Desktop** (completo, sólido 3D) e **Web/PWA** (leve, PC e celular). Uma licença vale para as duas |
| Cobrança | Assinatura anual **e** licença perpétua (com 1 ano de atualizações) |
| Conta | Separada do Casillas (projeto Supabase próprio); mesmo ecossistema, produtos complementares |
| Pagamento | Pix direto. Início: ativação manual num painel; depois, opcionalmente, gateway com confirmação automática |
| Trial | Libera tudo. Depois de expirar, continua funcionando com restrições |
| Repositório | O mesmo; novo projeto `CncStudio.Web` ao lado do desktop, com o mesmo Core |

## Fases

| Fase | Entrega | Status |
|---|---|---|
| 9 | Identidade visual no desktop: nome, ícones (.ico/.png gerados dos SVGs), instalador, README | ✅ |
| 10 | **Web — protótipo**: projeto `CncStudio.Web` (Avalonia no navegador); editor com edição pelo editor nativo do celular; backplot torno ZX e fresa 2,5D/topo/frente; simulação passo a passo (passe a passe nos ciclos) com alarme em erro; erros marcados no editor; menu Exibir; layout para celular em pé e deitado. CI publica no ramo `ci-web` | ✅ |
| 11 | **Web — estatísticas e folha de setup**: botão ⏱ no gráfico e Arquivos › Estatísticas (tempo de ciclo por corte/rápido/espera, cartão por ferramenta com operação, código, avanço, alcance, limites); Arquivos › Folha de setup (A4, ajustada ao celular, PDF/imprimir e compartilhar em PDF gerado no app). Padrão de comentário: `T0101 (OPERAÇÃO)` e na linha de baixo `(CÓDIGO DA FERRAMENTA)`. Depois do teste sem licença: só o tempo total; folha com marca d'água | ✅ |
| 12 | **PWA** no ar em https://casillas-code.pages.dev — manifesto, ícones, service worker (offline), botão Instalar; publicação automática no Cloudflare Pages a cada commit. Instalação, atualização e botão Abrir confirmados no Android | ✅ |
| 13 | **Contas e licenças**: Supabase (login e-mail/Google), trial de 14 dias, limite de dispositivos, restrições pós-trial; no desktop, licença assinada (Ed25519) com tolerância offline. Feito: projeto `Casillas Code`, tabelas, regras e funções (ver `supabase/README.md`); na web, entrar/criar conta/recuperar senha por e-mail, item Conta (situação, aparelhos, Sair da conta), 7 dias sem internet com a última confirmação, salvar/exportar bloqueados sem trial ou licença. Falta: licença no desktop | 🔶 |
| 14 | **Painel de ativação** (Pix manual), fora do app em https://casillas-code-painel.pages.dev (pasta `admin/`): entrar só como administrador; clientes com situação, ativar assinatura/perpétua com valor e referência do Pix, renovar, +1 aparelho, revogar, bloquear/liberar aparelho, estender teste, mensagem pronta para o WhatsApp. Preço sob consulta; aviso de pagamento pelo WhatsApp | ✅ |
| 15 | **Lançamento**: termos de uso (com aviso de que a simulação não garante ausência de colisão), política de privacidade (LGPD), página de vendas | |

## Restrições depois do trial (proposta)

| Recurso | Depois do trial |
|---|---|
| Abrir, editar, backplot, simulação | Liberado |
| Salvar / exportar | Bloqueado (ou só programas pequenos) |
| Folha de setup | Com marca d'água "Versão de avaliação" |
| Estatísticas por ferramenta, comparação | Bloqueado |

## O que depende do dono do produto

- Fase 14: definição de preços. E-mail do Casillas: casillas.usinagem.br@gmail.com (envio pelo Gmail configurado e testado).
- Fase 15: revisão jurídica dos termos e consulta ao contador sobre o recebimento.

## Melhorias da versão web (feitas)

- Versão na tela de carregamento e no app; aviso "Nova versão disponível" (compara com `version.txt`).
- Velocidade da simulação com − e +; botões arredondados com efeito mola e vibração opcional.
- Lembra o programa, a linha, a tela, a vista e a velocidade ao reabrir.
- Ferramenta mostrada como um ponto.
- Menu **Arquivos**: Biblioteca (pastas, salvar, salvar como, substituir, importar vários, renomear, excluir, compartilhar), Salvar, Salvar como, Compartilhar/exportar, Novo (colar), Exemplos.
- **Modo claro** (menu Exibir).
- **Exemplos por comando e máquina** (menu Arquivos › Exemplos): Fanuc torno (11), Fanuc fresa (12), Siemens torno (9), Siemens fresa (9) — um por ciclo e por método de repetição (M98 L, G65, WHILE, FOR, REPEAT, IF/GOTOB), todos sem erros nem avisos no simulador. Fontes em `samples/exemplos/`.
- **Funções do editor**: renumerar (início, incremento, todos os blocos ou só os que têm N), remover numeração e desfazer. P/Q do G70–G73, GOTO e M99 P acompanham a nova numeração; ao remover, os blocos que são destino mantêm o N.

Novidades no simulador (valem para desktop e web): G73 do torno (repetição de perfil); G73, G84, G85, G86 e G89 na fresa Fanuc; G04 U no torno; RND/CHR do Siemens (também no plano ZX do torno); diâmetro da ferramenta lido do comentário da troca ("FRESA D10") quando não há tabela; detecção de torno pelo T0101 do Fanuc; DEF do Siemens exige nome com duas letras (como a Sinumerik).

## Próximas melhorias da versão web

- Estatísticas e folha de setup (fase 11).
