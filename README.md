# NEXUS Admin

Aplicação administrativa desktop para um FXServer executado no mesmo computador. O executável oferece consulta de jogadores online (nome, ID e ping) e envio de avisos individuais. A ponte aceita somente conexões loopback, exige um token compartilhado e só executa as duas ações permitidas pelo resource.

## Compilar o executável

Com o SDK .NET 8 instalado, execute na raiz do projeto:

```powershell
dotnet publish .\desktop\NexusAdmin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\desktop\publish
```

O executável será gerado em `desktop\publish\NexusAdmin.exe`. A publicação self-contained inclui o runtime .NET necessário no próprio aplicativo.

## Instalação no FXServer

1. Copie a pasta deste resource para `resources\[local]\nexus_admin`.
2. Inicie `NexusAdmin.exe` no mesmo computador do FXServer.
3. Na aba **Configurações**, gere um token e copie as linhas mostradas para o `server.cfg`.
4. Coloque as linhas antes de `ensure nexus_admin`:

   ```cfg
   set nexus_admin_api_url "http://127.0.0.1:38765"
   set nexus_admin_api_token "COLE_AQUI_O_TOKEN_GERADO"
   add_ace resource.nexus_admin nexus_admin.view allow
   add_ace resource.nexus_admin nexus_admin.notice allow
   add_ace resource.nexus_admin nexus_admin.broadcast allow
   add_ace resource.nexus_admin nexus_admin.kick allow
   ensure nexus_admin
   ```

5. As permissões ACE acima autorizam o resource do painel, não usuários do jogo. Restrinja acesso ao computador que executa o app e proteja o `server.cfg`.
6. Inicie ou reinicie o resource e, no app, clique em **Iniciar ponte local**. O indicador mudará para conectado quando o resource autenticado começar a consultar a API.
7. Abra **Jogadores** e atualize a lista. A comunicação permite aviso individual, aviso global e expulsão do jogador selecionado; os dois últimos pedem confirmação antes de executar.
8. Se uma permissão ACE estiver ausente, o FXServer rejeitará a ação e o app exibirá o motivo.

O token não é salvo pelo app nem incluído no código-fonte. Informe novamente o token ao abrir o aplicativo. Use um token ASCII de 32 a 128 caracteres, contendo apenas letras, números, `_` ou `-`. Proteja o `server.cfg` e não compartilhe o token.

## API local

- `POST /bridge/poll`: consultada pelo resource do FXServer; entrega somente comandos enfileirados pelo app.
- `POST /bridge/results`: recebe a resposta do FXServer ao comando.
- `GET /health`: diagnóstico local.
- A API escuta em `127.0.0.1:38765`, exige `Authorization: Bearer <token>` nas rotas de ponte e recusa conexões que não sejam loopback.
- O resource aceita somente `getPlayers`, `sendNotice`, `sendBroadcast` e `kickPlayer`; não há execução arbitrária de comandos.
- Antes de listar jogadores, exige `nexus_admin.view`; antes de enviar avisos individuais, exige `nexus_admin.notice`; avisos globais exigem `nexus_admin.broadcast`; expulsões exigem `nexus_admin.kick`.
- O resource valida o ID conectado e limita avisos a 240 caracteres.
- O histórico de atividade do app fica apenas na memória durante a sessão atual.

O app e o FXServer precisam estar no mesmo computador nesta configuração. Não exponha a porta da ponte à rede. Não são implementados ESP, noclip, invulnerabilidade, teleporte, execução de comandos arbitrários ou manipulação de entidades.
