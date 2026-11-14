# KZINN ADMIN — FiveM

Painel desktop Windows + resource FiveM para administração de servidores nos quais você tem autorização.

## O que inclui

- Lista de jogadores online, ID e ping.
- Aviso individual e aviso global.
- Expulsão com confirmação.
- Cura, revive e congelar/liberar jogador.
- Histórico de atividade durante a sessão.
- Ponte HTTP autenticada por token, limitada a `127.0.0.1`.
- Permissões ACE verificadas no servidor.
- Compilação automática do `.exe` pelo GitHub Actions.

Este projeto não é um injetor universal. O resource `nexus_admin` precisa estar instalado no FXServer. Para usar em outro servidor autorizado, instale o resource nele e execute o painel na mesma máquina do FXServer.

## Instalação do resource

1. Copie `nexus_admin` para `resources/[local]/nexus_admin`.
2. Gere um token na tela do aplicativo.
3. Coloque no `server.cfg`, antes de `ensure nexus_admin`:

```cfg
set nexus_admin_api_url "http://127.0.0.1:38765"
set nexus_admin_api_token "COLE_SEU_TOKEN_AQUI"

add_ace resource.nexus_admin nexus_admin.view allow
add_ace resource.nexus_admin nexus_admin.notice allow
add_ace resource.nexus_admin nexus_admin.broadcast allow
add_ace resource.nexus_admin nexus_admin.kick allow
add_ace resource.nexus_admin nexus_admin.player_actions allow

ensure nexus_admin
```

4. Inicie/reinicie o resource.
5. Abra `KzinnAdmin.exe`, informe o mesmo token e clique em **Iniciar ponte**.
6. Abra a aba Jogadores e clique em **Atualizar lista**.

## Compilar localmente

Requer Windows e .NET 8 SDK:

```powershell
dotnet publish .\desktop\KzinnAdmin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

O executável estará em `publish\KzinnAdmin.exe`.

## Gerar pelo GitHub Actions

Faça push deste projeto para o GitHub. A workflow `.github/workflows/build.yml` compila o app para Windows e publica `KzinnAdmin.exe` como artefato na execução.

## Segurança e limites

- O painel e o FXServer devem estar na mesma máquina nesta versão.
- A API escuta somente em `127.0.0.1`; não encaminhe a porta 38765 para a internet.
- O token é secreto: não o publique nem o coloque em prints.
- As permissões ACE são aplicadas ao resource e devem ser revisadas pelo dono do servidor.
- Não inclui bypass de anti-cheat, ESP oculto, manipulação de servidores de terceiros ou execução arbitrária de comandos.
