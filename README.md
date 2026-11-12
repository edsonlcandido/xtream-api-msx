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

- Início com entradas para canais ao vivo, filmes e séries.
- Categorias dinâmicas do painel, lista de canais e EPG quando disponível na origem.
- Catálogo de filmes e séries em cartazes paginados.
- Detalhes de séries, temporadas e episódios.
- Busca de canais, filmes e séries.
- Endpoint compatível com `player_api.php`, health check e diagnóstico da conexão com o painel.

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

Os IDs de categoria em `appsettings.json` são valores de exemplo: ajuste-os aos IDs do seu painel. Se um ID configurado não existir, a primeira categoria disponível é usada. Opções de paginação, cache e limites de catálogo — como `PagingLimit`, `SearchLimit`, `SearchIndexCacheMinutes`, `MaxPosterItems` e `MaxChannelItems` — também podem ser ajustadas em `appsettings.json` ou por variáveis `Xtream__...`.

## Endpoints principais

| Rota | Finalidade |
|---|---|
| `GET /msx/start.json` | Entrada configurada no Media Station X. |
| `GET /msx/menu.json` | Menu geral do MSX, disponível opcionalmente. |
| `/msx/content/...` e `/msx/service/...` | Telas, categorias, paginação, busca e detalhes do catálogo. |
| `GET /player_api.php?...` | API Xtream compatível, encaminhada ao painel de origem. |
| `GET /health` | Verificação de disponibilidade. |
| `GET /msx/diagnostico.json` | Diagnóstico de acesso ao painel e à configuração de rede. |

## Transporte e segurança

- As URLs públicas geradas pelo app usam `http` por padrão. Se o endpoint público do MSX usar HTTPS, configure `Xtream__PublicScheme=https`. Streams HTTP carregados por uma página HTTPS podem sofrer bloqueio de *mixed content*; o Xtream MSX não retransmite vídeo para contornar isso.
- Credenciais no subdomínio podem aparecer em consultas DNS e logs de acesso do proxy. Proteja esses logs, não publique URLs de clientes e use HTTPS quando toda a cadeia de reprodução for compatível.
- Armazene variáveis de ambiente e arquivos de secrets fora do repositório e limite o acesso ao painel e ao servidor.

## Diagnóstico

Se o catálogo não carregar, confirme que o servidor alcança o `player_api.php` do painel e que a conta tem acesso às ações necessárias. `/msx/diagnostico.json` ajuda a verificar a resolução DNS e a conectividade vistas pelo servidor. Se a API funcionar, mas a reprodução falhar, confirme se a TV consegue acessar diretamente os endereços de mídia retornados pelo painel e se a base de reprodução em `server_info` está correta; o tráfego de vídeo não passa pelo Xtream MSX.
