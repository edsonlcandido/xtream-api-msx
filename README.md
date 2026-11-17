# Xtream MSX


**Use o Media Station X como interface para o catálogo do seu próprio painel IPTV.**


Xtream MSX conecta uma instalação do Media Station X (MSX) a um painel compatível com a API Xtream Codes. Cada revendedor configura a URL do seu painel e pode oferecer aos clientes uma navegação de TV ao vivo, filmes e séries pelo MSX.


> Xtream MSX é uma camada de integração, não um serviço de IPTV: não fornece canais, assinaturas, contas ou arquivos de mídia. Use apenas painéis e conteúdo que você está autorizado a distribuir.


## Como funciona


```text

Media Station X  →  Xtream MSX  →  API do painel do revendedor

       └──────────── URLs de reprodução do painel ────────────┘

```


- O MSX consulta o Xtream MSX, que busca categorias e catálogo no painel configurado e monta as telas no formato esperado pelo Media Station X.

- Cada implantação se conecta a uma URL base de painel (`BASE_URL`). Diferentes contas desse mesmo painel podem ser atendidas usando as credenciais de cada cliente.

- Os links de reprodução apontam para o painel de origem. O Xtream MSX não retransmite nem transcodifica o vídeo.

- A aplicação também oferece `/player_api.php` como uma API Xtream compatível, encaminhando as ações ao painel e completando os links `direct_source`.


## Recursos


- Início com entradas para canais ao vivo, filmes e séries — e, rolando para baixo, a faixa **Últimos assistidos** com o que o cliente acabou de assistir.

- Categorias dinâmicas do painel, lista de canais e EPG quando disponível na origem.

- Catálogo de filmes e séries em cartazes paginados.

- Detalhes de séries, temporadas e episódios.

- Busca de canais, filmes e séries.

- Endpoint compatível com `player_api.php`, health check e diagnóstico da conexão com o painel.


## Continuar assistindo: a faixa "Últimos assistidos"


A tela inicial tem **duas seções empilhadas na vertical**: as entradas de canais ao vivo, filmes e séries e, rolando para baixo, a seção **"Últimos assistidos"** com o que o cliente acabou de assistir.


```text

┌────────────────────────────────────────────┐  ← seção 1

│   [ Canais ao vivo ] [ Filmes ] [ Séries ] │

└────────────────────────────────────────────┘

                    ↓ rola para baixo

┌────────────────────────────────────────────┐  ← seção 2

│  ÚLTIMOS ASSISTIDOS                         │    teaser "0,0,12,1"

│  [c][c][c][c][c][c]                        │    até 6 cartazes "{2*i},1,2,4"

└────────────────────────────────────────────┘

```


- O clique no item reabre o conteúdo: filme abre o filme, série abre a página da mídia (com as temporadas) e canal acessa o canal.

- Sem histórico, a segunda seção **não existe** — a tela inicial continua sendo só a primeira.


### Como o app sabe o que foi assistido


O playback do MSX vai direto do app para o painel, então o app não "vê" a reprodução. Quem avisa é o **próprio MSX**: todo item reproduzível carrega a extended property


```json

"properties": { "trigger:start": "execute:service:{host}/msx/history/record?kind=movie&id=415190&category_id=25" }

```


`trigger:start` dispara quando o vídeo **começa**, e o MSX chama o serviço `execute:service:` (o mesmo mecanismo do EPG). Os parâmetros são curtos de propósito: `kind` (`movie`/`series`/`live`), `id` (`stream_id` ou `series_id`) e `category_id` (a categoria de origem — com ela o app acha a capa e a página da série).


O gancho está nos cartazes de **filmes** (grade e busca), nas linhas de **canais** (lista e busca) e nas linhas de **episódio** — no episódio, quem entra no histórico é a série.


### Como a faixa se atualiza quando o cliente volta para o Início


Este é o ponto mais chato do recurso: **o MSX não pede de novo a URL da tela quando o cliente volta** — ele restaura a tela que já estava montada. Sem mexer nisso, a seção "Últimos assistidos" continuaria congelada no que o app serviu na primeira vez.


A tela do Início resolve isso com três peças:


| Peça | Para que serve |

|---|---|

| `"reference": "…/msx/content/home-data.json"` | O conteúdo real fica em outro arquivo. É o que permite **recarregar** a tela (`reload:content`) mesmo tendo sido carregada no startup — o MSX só recarrega conteúdo de startup que vem por `reference`. |

| `"ready": { "action": "execute:service:…/msx/history/refresh" }` | Roda **toda vez que a tela é exibida** (inclusive na volta do player). O serviço devolve `reload:content` se o histórico mudou, ou `[]` (nada) se não mudou. |

| `"live"` no primeiro card | Mesma consulta, para versões do MSX que ainda não conhecem o objeto `ready`. |


O `cache: false` + `reuse: false` no root completam: sem o `reuse: false` o MSX reaproveita a tela atual (atalho de "mesma URL") sem nem buscar o servidor.


**Sem laço:** o app guarda a "versão" do histórico (quantos itens + quando o mais recente foi assistido) de cada cliente. A tela do Início grava a versão que ela serviu; o serviço de refresh só devolve `reload:content` se a versão atual for diferente da gravada — e aí grava a nova. Na prática são 2 POSTs por exibição da tela (o `ready` e o `live`), e nenhuma recarga extra.


### Onde o histórico fica


Um arquivo JSON por cliente (`{HISTORY_PATH}/{usuario}.json`), gravado de forma atômica (`.tmp` + move). O caminho vem da seção `History` do `appsettings.json` e pode ser sobrescrito pela variável de ambiente **`HISTORY_PATH`**:


```json

"History": {

  "Path": "data/history",

  "MaxItems": 50,

  "MaxAgeDays": 30,

  "StripItems": 6

}

```


- **Monte um volume** no container apontando para `/data` e defina `HISTORY_PATH=/data/history`: sem volume, o histórico se perde a cada recriação do container.

- Se o disco não for gravável, o app **segue só em memória** (aviso no log) — nada quebra.

- Os metadados (título, capa, ano, nota) são gravados junto, então a faixa **aparece mesmo com o painel fora do ar**.


## Compatibilidade


O painel precisa oferecer uma API compatível com Xtream Codes por `player_api.php`. A integração usa ações comuns para consultar categorias, canais, filmes, séries e detalhes de séries; o EPG depende de o painel fornecer dados para `get_short_epg`. Painéis derivados podem variar no formato das respostas, então a compatibilidade deve ser confirmada com a conta e o painel que você pretende usar.


Uma instalação aponta para **um painel de origem**. Para conectar revendedores a painéis diferentes, implante uma instância por painel e configure uma `BASE_URL` para cada uma.


## Instalação com Docker


O container escuta na porta `8080`. Crie um arquivo de ambiente fora do repositório, por exemplo `xtream-msx.env`:


```dotenv

BASE_URL=https://painel.seu-provedor.example

Xtream__PublicBaseUrl=https://msx.seu-dominio.example

Xtream__PublicScheme=https


# Opcional: conta padrão usada quando a requisição não traz credenciais.

# Para vários clientes, prefira as credenciais por subdomínio descritas abaixo.

# Xtream__Username=

# Xtream__Password=


# Opcional: onde o histórico de "últimos assistidos" é gravado.

# Use um volume montado em /data (veja "Continuar assistindo").

# HISTORY_PATH=/data/history

```


O exemplo pressupõe que um proxy reverso termina o TLS do domínio público e encaminha as

requisições ao container em HTTP na porta `8080`. Ajuste `BASE_URL` ao esquema usado pelo

painel de origem e `Xtream__PublicBaseUrl` / `Xtream__PublicScheme` ao endereço público do

MSX.


Construa e execute:


```bash

docker build -t xtream-msx .

docker run -d \

  --name xtream-msx \

  --restart unless-stopped \

  -p 8080:8080 \

  --env-file ./xtream-msx.env \

  xtream-msx

```


Para manter o histórico de "últimos assistidos" entre recriações do container, monte um volume:


```bash

docker run -d \

  --name xtream-msx \

  --restart unless-stopped \

  -p 8080:8080 \

  --env-file ./xtream-msx.env \

  -v xtream-msx-data:/data \

  xtream-msx

```


Confirme que a aplicação está respondendo:


```bash

curl http://localhost:8080/health

```


O projeto também pode ser executado com o SDK do .NET 10. Se usar um proxy reverso, encaminhe o tráfego para a porta `8080` e preserve `X-Forwarded-Host` e `X-Forwarded-Proto`. Configure `Xtream__PublicBaseUrl` com o endereço público do app quando o proxy não preservar o host original.


## Conectar o Media Station X


Para usar credenciais diferentes por cliente:


1. Aponte um registro DNS curinga, como `*.msx.seu-dominio.example`, para o proxy ou servidor que publica o Xtream MSX.

2. No Media Station X, abra **Settings → Start Parameter → Setup**.

3. Informe o host no formato `<usuario>-<senha>.msx.seu-dominio.example`, sem `http://` ou `https://`, e confirme.

4. O MSX buscará `/msx/start.json` e abrirá o catálogo associado àquela conta.


O prefixo do host é interpretado como credencial: o parser atual separa usuário e senha no primeiro hífen e exige pelo menos três caracteres em cada parte. Portanto, o usuário não deve conter hífen. Para uma única conta fixa, configure `Xtream__Username` e `Xtream__Password` no servidor e use o domínio sem prefixo de credencial.


O app também aceita `?u=<usuario>&p=<senha>` para testes. Não use credenciais na query string em links de produção: URLs podem ser registradas no histórico do navegador e nos logs.


## Configuração


As variáveis podem ser informadas no arquivo de ambiente ou como opções do ASP.NET Core (`Xtream__NomeDaOpcao`). Não versione credenciais ou arquivos `.env`.


| Variável / opção | Uso |

|---|---|

| `BASE_URL` ou `Xtream__BaseUrl` | URL base do painel Xtream de origem. |

| `FALLBACK_URL` | Bases alternativas, separadas por vírgula, para o painel de origem. |

| `PROXY_URL` | Relay opcional que disponibiliza `player_api.php` quando o servidor não alcança o painel diretamente. |

| `Xtream__Username` / `Xtream__Password` | Credenciais padrão/fallback para uma conta. |

| `Xtream__PublicBaseUrl` | URL pública do Xtream MSX, útil atrás de proxy reverso. |

| `Xtream__PublicScheme` | Esquema usado nas URLs geradas. O padrão atual é `http`. |

| `Xtream__UseServerInfo` | Usa `server_info` do painel para montar URLs de reprodução (padrão: `true`). |

| `Xtream__PlaybackBaseUrl` | Base fixa para URLs de reprodução; quando definida, tem prioridade sobre `server_info`. |

| `Xtream__DefaultLiveCategory` | Categoria inicial de canais ao vivo. |

| `Xtream__DefaultVodCategory` | Categoria inicial de filmes. |

| `Xtream__DefaultSeriesCategory` | Categoria inicial de séries. |

| `HISTORY_PATH` | Pasta do histórico de "últimos assistidos" (fora do repositório). Sobrescreve `History:Path`. |

| `History:MaxItems` / `History:MaxAgeDays` | Quanto o histórico guarda por cliente. |

| `History:StripItems` | Quantos itens aparecem na faixa "Últimos assistidos" (6 = uma faixa cheia na grade 12×6). |


Os IDs de categoria em `appsettings.json` são valores de exemplo: ajuste-os aos IDs do seu painel. Se um ID configurado não existir, a primeira categoria disponível é usada. Opções de paginação, cache e limites de catálogo — como `PagingLimit`, `SearchLimit`, `SearchIndexCacheMinutes`, `MaxPosterItems` e `MaxChannelItems` — também podem ser ajustadas em `appsettings.json` ou por variáveis `Xtream__...`.


## Endpoints principais


| Rota | Finalidade |

|---|---|

| `GET /msx/start.json` | Entrada configurada no Media Station X. |

| `GET /msx/menu.json` | Menu geral do MSX, disponível opcionalmente. |

| `/msx/content/...` e `/msx/service/...` | Telas, categorias, paginação, busca e detalhes do catálogo. |

| `GET /msx/content/home-data.json` | Conteúdo real da tela do Início (é o `reference` do `home.json`). |

| `GET\|POST /msx/history/record?kind=…&id=…&category_id=…` | Grava um item em "últimos assistidos" (chamado pelo `trigger:start` do MSX). |

| `GET\|POST /msx/history/refresh` | Consulta do `ready` da tela do Início: devolve `reload:content` se o histórico mudou (ou `[]` se não mudou). |

| `GET /msx/history` | Inspeção do histórico do cliente (JSON simples, para depuração). |

| `GET /player_api.php?...` | API Xtream compatível, encaminhada ao painel de origem. |

| `GET /health` | Verificação de disponibilidade. |

| `GET /msx/diagnostico.json` | Diagnóstico de acesso ao painel e à configuração de rede. |


## Transporte e segurança


- As URLs públicas geradas pelo app usam `http` por padrão. Se o endpoint público do MSX usar HTTPS, configure `Xtream__PublicScheme=https`. Streams HTTP carregados por uma página HTTPS podem sofrer bloqueio de *mixed content*; o Xtream MSX não retransmite vídeo para contornar isso.

- Credenciais no subdomínio podem aparecer em consultas DNS e logs de acesso do proxy. Proteja esses logs, não publique URLs de clientes e use HTTPS quando toda a cadeia de reprodução for compatível.

- Armazene variáveis de ambiente e arquivos de secrets fora do repositório e limite o acesso ao painel e ao servidor.


## Diagnóstico


Se o catálogo não carregar, confirme que o servidor alcança o `player_api.php` do painel e que a conta tem acesso às ações necessárias. `/msx/diagnostico.json` ajuda a verificar a resolução DNS e a conectividade vistas pelo servidor. Se a API funcionar, mas a reprodução falhar, confirme se a TV consegue acessar diretamente os endereços de mídia retornados pelo painel e se a base de reprodução em `server_info` está correta; o tráfego de vídeo não passa pelo Xtream MSX.


Se a faixa "Últimos assistidos" não aparecer, o fluxo de verificação é: `GET /msx/history` mostra o que foi gravado? O `home.json` devolve a seção? Ao voltar do player, o MSX re-busca o `home-data.json` e re-executa o `ready`? O `refresh` devolveu `reload:content`?

