# StorageTerm

Gerenciador de terminais SSH para Windows, feito para alternar entre **`storage-lan`** (rede local) e **`storage`** (Tailscale/externo) sem trocar de janela, perder sessão ou estourar a tela com abas.

## Destaques

- **Interface minimalista**: barra de 44 px, sem logo na barra, tema **grafite monocromático** (sem verde) e ações secundárias no menu **⋯**; mais área para o terminal.
- **Auto-update**: a versão instalada baixa releases novas do GitHub, confere o SHA-256 e aplica sozinha ao sair (ou em **⋯ → Atualizar agora**).
- **1 a 6 terminais**, sempre começando com **um**. `Ctrl+T` abre o próximo e o mosaico se reorganiza sozinho.
- **Abre direto no SSH**: o primeiro painel já executa `ssh storage-lan` ou `ssh storage`; shell local só no modo `--demo`.
- **Identificação correta**: usuário, host, porta e chave vêm do `~/.ssh/config` e aparecem no título da janela, no cabeçalho do painel e nas configurações.
- **Layout inteligente**: 1 inteiro, 2 lado a lado, 3 com um grande + dois empilhados, 4 em 2×2, 5 com uma coluna extra e 6 em 3×2.
- **Conexão automática**: testa `192.168.1.30:22`; se responder, usa `storage-lan`; se não, cai para `storage` (Tailscale). O painel mostra **LAN** ou **EXT**.
- **Troca por painel**: clique no selo LAN/EXT para forçar local/externo só naquela sessão, sem mexer nas outras.
- **Reconexão automática** com espera progressiva (1s, 2s, 4s, 8s, 16s), reavaliando LAN × externo a cada tentativa.
- **Fica na bandeja**: fechar a janela não encerra os SSH. Só **Sair** encerra.
- **Inicia com o Windows** no logon, já minimizado na bandeja (`--tray`).
- **Instância única**: abrir o `.exe` de novo só mostra a janela que já está rodando.
- **Evita a suspensão do Windows** enquanto houver sessões (opcional, ligado por padrão), para os processos `ssh` continuarem vivos.
- **Terminal real** (parser/renderer do Windows Terminal + ConPTY), com cores, mouse, scrollback e aplicações de tela cheia.
- Usa o seu `~/.ssh/config`; **não armazena senhas nem chaves**.

## Requisitos

- Windows 10 1809+ ou Windows 11.
- Cliente OpenSSH do Windows (`ssh.exe` em `C:\Windows\System32\OpenSSH`).
- `~/.ssh/config` com os perfis `storage-lan` e `storage` e a chave `id_ed25519` já cadastrada no servidor.
- Para compilar: **.NET 10 SDK** (já instalado nesta máquina).
- Para a versão em `dist` (framework-dependent): **.NET 10 Desktop Runtime**. A instalação via `install.ps1` gera uma versão **self-contained**, que não precisa do runtime.

## Rodar agora

O build pronto está em:

```text
dist\StorageTerm\StorageTerm.exe
```

Dê duplo clique ou execute:

```powershell
.\dist\StorageTerm\StorageTerm.exe
```

Modo demonstração (usa `cmd.exe` local, sem SSH, ótimo para testar o layout):

```powershell
.\dist\StorageTerm\StorageTerm.exe --demo
.\dist\StorageTerm\StorageTerm.exe --demo --panes=6
```

## Instalar (com autostart)

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

O que o instalador faz:

- publica uma versão **self-contained** em `%LOCALAPPDATA%\Programs\StorageTerm`;
- cria o atalho **StorageTerm** no Menu Iniciar;
- registra `StorageTerm` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` como `"...\StorageTerm.exe" --tray`;
- abre o app no final.

Opções: `-NoAutoStart`, `-NoLaunch`, `-FrameworkDependent`, `-InstallDir "D:\Apps\StorageTerm"`.

Desinstalar:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

Para apagar também configurações e logs: `.\uninstall.ps1 -RemoveData`.

## Atualizações automáticas

Repositório: **https://github.com/jhordan-pastorello/gerenciado-terminal-ssh**

A versão **instalada** (a que tem o marcador `install.json`, criado pelo `install.ps1`):

1. verifica o **GitHub Releases** 12 s após abrir e a cada **6 h**;
2. se houver uma versão maior, baixa `StorageTerm-win-x64.zip` e confere o **SHA-256** publicado na release;
3. aplica a atualização:
   - **ao sair** (silenciosamente — na próxima abertura já é a versão nova);
   - ou em **⋯ → Atualizar para vX.Y.Z** (confirma, encerra as sessões e reinicia o app).

O menu **⋯** mostra a versão atual e tem **Verificar atualizações**. A versão em `dist` e builds de desenvolvimento **não** se auto-atualizam (falta o `install.json`).

### Publicar uma nova versão

O workflow `.github/workflows/release.yml` roda testes, gera o build self-contained, o `.zip`, o `.sha256` e cria a release:

```powershell
# opcional: atualize <Version> no StorageTerm.csproj
git tag v1.0.1
git push origin v1.0.1
```

Os apps instalados passam a ver a `v1.0.1` e se atualizam sozinhos. A tag precisa ser `vX.Y.Z` estável (sem `-beta`).

## Atalhos

| Atalho | Ação |
|---|---|
| `Ctrl+T` | Novo terminal (até 6) |
| `Ctrl+Q` | Fechar o terminal ativo (com confirmação, se configurado) |
| `Ctrl+Shift+W` | Fechar o terminal ativo (alternativa) |
| `Ctrl+Shift+1` … `Ctrl+Shift+6` | Focar o terminal N |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Próximo / anterior |
| `Ctrl+Shift+C` | Copiar a seleção |
| `Ctrl+Shift+V` | Colar |
| `F11` | Maximizar / restaurar |

`AltGr` (Ctrl+Alt) **nunca é capturado**: o app compõe o caractere com o layout ABNT2 e envia direto ao PTY, então `/`, `?`, `°`, `!`, `@`, `#` funcionam inclusive dentro do tmux. `Ctrl+Q` só fecha o painel quando `Alt` **não** está pressionado; `Ctrl+Shift+W` é a alternativa.

Os atalhos são registrados como *hotkeys* enquanto a janela está ativa. Isso faz `Ctrl+T` funcionar mesmo com o terminal nativo focado (dentro do SSH), sem enviar o atalho para o servidor.

## Layouts

```text
1 painel                 2 painéis                3 painéis
┌───────────────┐        ┌───────┬───────┐        ┌───────┬───────┐
│       1       │        │   1   │   2   │        │   1   │       │
└───────────────┘        └───────┴───────┘        ├───────┤   3   │
                                                  │   2   │       │
                                                  └───────┴───────┘

4 painéis                5 painéis                6 painéis
┌───────┬───────┐        ┌─────┬─────┬─────┐      ┌─────┬─────┬─────┐
│   1   │   2   │        │  1  │  2  │     │      │  1  │  2  │  3  │
├───────┼───────┤        ├─────┼─────┤  5  │      ├─────┼─────┼─────┤
│   3   │   4   │        │  3  │  4  │     │      │  4  │  5  │  6  │
└───────┴───────┘        └─────┴─────┴─────┘      └─────┴─────┴─────┘
```

- Em **3** e **5**, o terminal **mais novo** ocupa o espaço grande/coluna extra. Os demais ficam empilhados na ordem de criação.
- Em **4** e **6**, a ordem é de leitura.
- Ao fechar um painel, os restantes refluem mantendo a ordem. O limite é **6**; a sétima tentativa mostra um aviso.
- Abrir com um terminal é o padrão. `--panes=N` existe apenas para testes/demonstração.

## LAN × externo

No modo **Automático** (padrão):

1. o app faz um TCP connect em `192.168.1.30:22` com timeout de **800 ms**;
2. se responder, abre `ssh storage-lan`;
3. se não responder, abre `ssh storage` (Tailscale);
4. a sonda é refeita a cada **30 s** e antes de cada reconexão.

O selo no painel mostra o resultado real daquela sessão. Clique nele para:

- **Conexão automática**;
- **Forçar LAN (`storage-lan`)**;
- **Forçar externo (`storage`)**.

O botão de modo na barra superior muda o padrão **global** para as próximas sessões. Sessões já abertas não são derrubadas sem aviso; use o selo do painel ou **⋯ → Reconectar todos**.

Teste manual: **Configurações → Testar agora** mostra a latência da LAN (ex.: `36 ms`) ou avisa que vai usar o perfil externo.

Opções SSH aplicadas: `ConnectTimeout=8`, `ServerAliveInterval=30`, `ServerAliveCountMax=3`, `TCPKeepAlive=yes` (além do que já estiver no `~/.ssh/config`).

## Abertura direta e identificação

Ao abrir o app (sem `--demo`), o primeiro terminal **já inicia o ssh automaticamente**. O app escolhe o perfil, e a identificação resolvida do `~/.ssh/config` aparece em três lugares:

- **título da janela**: `StorageTerm — storage@storage-lan`;
- **cabeçalho do painel** (tooltip): `storage@storage-lan · 192.168.1.30:22 · id_ed25519`;
- **Configurações → Conexão SSH**: perfis detectados, por exemplo:
  `LAN: storage@storage-lan · 192.168.1.30:22 · id_ed25519`
  `Externo: storage@storage-dev.<tailnet>.ts.net:22 · id_ed25519`.

O comando continua sendo `ssh <alias>`, então o OpenSSH resolve `HostName`, `User`, `Port`, `IdentityFile` e qualquer outra diretiva do seu config. O app apenas lê esses dados para exibir a identificação; não guarda chaves nem senhas.

## Sessões, bandeja e suspensão

- O processo principal do app é o "daemon": ele é dono dos PTYs/ConPTY e dos processos `ssh`.
- Fechar a janela (**X**) esconde para a bandeja e **mantém as sessões**.
- **`Ctrl+Q`** (ou **`Ctrl+Shift+W`**) fecha **o terminal ativo**. Se for o último, o app continua aberto (tela vazia + bandeja); use **Sair** no menu do ícone para encerrar o app e os SSH.
- **Sair** no menu do ícone da bandeja encerra os SSH e o app.
- Iniciar com o Windows usa `--tray`: a janela abre oculta e já cria a primeira sessão.
- Uma segunda execução do `.exe` apenas mostra a janela da instância existente.
- Com **Manter o PC acordado** ligado, o app chama `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` enquanto houver sessões. Isso impede a **suspensão por inatividade**; não impede fechar a tampa nem um **Suspender** explícito.
- Se o Windows suspender, a conexão TCP cai — mas com a **sessão persistente (tmux)** o trabalho continua rodando no servidor. Ao acordar, o app recebe o evento de retorno, reata a sessão e você continua de onde parou.
- Após 5 tentativas rápidas sem sucesso, a reconexão pausa e o painel mostra **desconectado**. Clique em **↻** para recomeçar.

### Sessão persistente (tmux)

Por padrão, cada painel abre/reatar uma sessão `tmux` no servidor (`storage-1`, `storage-2`, …):

- suspender o notebook, perder a rede, fechar o painel ou reiniciar o app **não encerra o trabalho** no servidor;
- ao reconectar/acordar, o app usa `tmux new-session -A` e volta para a mesma sessão;
- o retorno da suspensão (`WM_POWERBROADCAST`) dispara uma reconexão automática após alguns segundos;
- a barra de status do tmux fica oculta e as bordas em cinza, mantendo o visual limpo e monocromático;
- o app fixa `escape-time 500` e `focus-events off` no tmux para evitar lixo de sequências do terminal ao reatar (ex.: `[>84;0;0c`);
- se o servidor não tiver tmux, o comando cai para um shell bash normal.

A opção fica em **Configurações → Sessões → Sessão persistente no servidor (tmux)** (ligada por padrão). O tmux já está instalado no seu servidor.

## Configurações

Abra pelo botão **⋯** no canto superior direito → **Configurações** (ou pelo menu do ícone da bandeja).

- **Modo global**: Automático / Sempre LAN / Sempre externo.
- **Aliases SSH**: padrão `storage-lan` e `storage`.
- **Host/porta de teste da LAN**: padrão `192.168.1.30:22`.
- **Reconectar automaticamente** quando a conexão cair.
- **Sessão persistente no servidor (tmux)** — mantém o trabalho através de suspensão/quedas.
- **Pedir confirmação** ao fechar um terminal conectado.
- **Manter o PC acordado** enquanto houver sessões.
- **Iniciar com o Windows** (Run do usuário) e **iniciar minimizado**.
- **Fechar a janela para a bandeja**.
- **Fonte e tamanho** do terminal (se a fonte não existir, cai para Consolas).

As configurações são gravadas em:

```text
%LOCALAPPDATA%\StorageTerm\settings.json
```

Logs (rotação automática em 2 MB):

```text
%LOCALAPPDATA%\StorageTerm\storage-term.log
```

Para resetar tudo: saia pelo menu da bandeja e apague `settings.json`. As sessões e o servidor não são alterados.

## Build, publicação e testes

```powershell
# compilar e rodar
dotnet build .\StorageTerm.csproj -c Debug
dotnet run --project .\StorageTerm.csproj -- --demo

# publicar a versão pequena (exige .NET 10 Desktop Runtime no destino)
.\publish.ps1

# publicar autocontida (não exige runtime no destino; ~150 MB)
.\publish.ps1 -SelfContained

# testes
dotnet test .\tests\StorageTerm.Tests\StorageTerm.Tests.csproj
```

A solução `StorageTerm.slnx` inclui o app e os testes (34 testes de layout e validação de comandos SSH).

## Arquitetura

- **WPF / .NET 10** (`net10.0-windows`, x64).
- **[WPF-UI.Terminal](https://www.nuget.org/packages/WPF-UI.Terminal)** 1.0.39, que encapsula o controle oficial do Windows Terminal para WPF (**`CI.Microsoft.Terminal.Wpf`**) e o **`Microsoft.Windows.Console.ConPTY`**.
- O renderer/parser nativo do Windows Terminal desenha o terminal; o ConPTY executa `ssh.exe`.
- O processo WPF é o dono das sessões e fica na bandeja (não há serviço/daemon separado).
- `System.Windows.Forms.NotifyIcon` para a bandeja, `SetThreadExecutionState` para o *keep-awake*, `RegisterHotKey` para os atalhos com o terminal focado, mutex + named pipe para instância única.
- Leiaute em uma grade lógica 6×2 (`Layout/LayoutEngine.cs`), sem reposicionar processos ou recriar PTYs ao reorganizar.

## Solução de problemas

**AltGr / ABNT2 (`/`, `?`, `°`, `!`, `@`) não digitam**
Atualize para a versão **1.0.3+**. Ela usa o modo de entrada de texto do Windows Terminal e um hook de AltGr que compõe o caractere e o envia direto ao PTY; `Ctrl+Alt+Q/W/E` voltam a produzir `/`, `?`, `°` inclusive no tmux. `Ctrl+Q` só fecha o painel quando Alt **não** está pressionado.

**Ao abrir/reatar aparece lixo tipo `[>84;0;0c` ou `?61;6;7;…c`**
Sequências de resposta do terminal vazando para o shell ao reatar o tmux. Atualize para **1.0.4+**: o app fixa `escape-time 500` e `focus-events off` no servidor, que é a correção recomendada para Windows Terminal/ConPTY + tmux.

**Suspender o notebook derruba o SSH**
A conexão TCP não sobrevive à suspensão. Ative **Sessão persistente (tmux)** (padrão): o trabalho continua no servidor e o app reata ao acordar. Sem tmux, o shell é recriado e o que estava rodando se perde.

**O SSH pede senha**
A chave `id_ed25519` não está autorizada no servidor. O prompt aparece dentro do próprio terminal; o app não guarda senha. Corrija o `authorized_keys` no servidor.

**Alerta de host key**
O `ssh` mostra o prompt normal de `known_hosts`. Só remova a entrada antiga se tiver certeza de que o servidor mudou.

**Sempre cai para externo**
Confirme `192.168.1.30:22` na rede local e use **Testar agora**. Para o perfil externo, o Tailscale precisa estar conectado. Você pode forçar LAN no selo do painel.

**Autostart não abre**
Verifique `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → valor `StorageTerm`. O app só considera o autostart ativo quando o caminho aponta para o `.exe` atual; reinstale ou ative de novo em Configurações.

**Erro de DLL nativa (`Microsoft.Terminal.Control.dll`)**
Instale o **Microsoft Visual C++ 2015–2022 Redistributable (x64)**. O Windows 11 desta máquina já possui.

**SmartScreen**
O executável não é assinado. Use "Mais informações → Executar assim mesmo" ou assine o binário em produção.

**Quero encerrar tudo**
Ícone da bandeja → **Sair**. Fechar a janela apenas esconde. Se o app estiver travado, finalize `StorageTerm.exe` no Gerenciador de Tarefas (isso encerra os SSH).

**Onde ver o que aconteceu**
`%LOCALAPPDATA%\StorageTerm\storage-term.log` registra sonda de LAN, comandos (sem senhas), quedas e reconexões.

## Segurança e limitações

- O app roda como o usuário atual (`asInvoker`), sem administrador.
- Aliases SSH são validados (`A-Z a-z 0-9 . _ @ : -`) antes de ir para a linha de comando, evitando injeção.
- Nenhuma senha, chave privada ou conteúdo de sessão é gravado. O log não registra a saída do terminal.
- Limite rígido de **6** terminais; sem abas, sem divisão manual, sem SFTP.
- Sessões não sobrevivem a **Sair**, logoff, reboot ou suspensão forçada; o autostart + reconexão as recria.
- Build apenas **win-x64** (o pacote nativo também tem arm64, mas o projeto está fixado em x64).
- Sem assinatura de código.

## Licenças

- StorageTerm: MIT (veja `LICENSE`).
- WPF-UI.Terminal: MIT.
- `CI.Microsoft.Terminal.Wpf`, `Microsoft.Windows.Console.ConPTY` e Windows Terminal: componentes Microsoft/MIT.

