# PZ Server Manager — memória do projeto para agentes

Leia isto antes de qualquer tarefa. Ao terminar, atualize **Histórico**, **Pendências** e **Armadilhas** com itens curtos, sem logs. Converse em português do Brasil; interface e mensagens em pt-BR; código em inglês.

## O que é

Painel web .NET 8 com Blazor Server interativo e xUnit para administrar um servidor Project Zomboid Build 42 em Debian 13. O painel roda como `pzmanager`, o jogo como `pzserver.service`, e a integração usa systemd/polkit, journalctl, FIFO, RCON, arquivos INI/SandboxVars, SteamCMD e dados locais. O modo antigo de instâncias multiplataforma ainda compila; Windows completo está no roadmap.

## Estrutura

- `src/PzServerManager.Domain`: entidades e regras centrais.
- `src/PzServerManager.Application`: contratos, INI/SandboxVars, RCON, mods, backups, automação e Cardinal.
- `src/PzServerManager.Infrastructure`: systemd, journal, FIFO, RCON, SQLite somente leitura, SteamCMD/Workshop e stores atômicos.
- `src/PzServerManager.Web`: páginas Blazor e serviços em segundo plano.
- `tests/PzServerManager.Tests`: testes xUnit e fixtures exclusivamente sintéticos.
- `deploy/linux`: templates, units e scripts de referência; `docs/` contém a preparação genérica do Debian.

## Como compilar e testar

```powershell
& 'C:\Program Files (x86)\dotnet\dotnet.exe' test PzServerManager.sln -m:1 -nodeReuse:false
& 'C:\Program Files (x86)\dotnet\dotnet.exe' build-server shutdown
```

Não adicione pacotes NuGet, não rode git, não publique e não altere servidores reais.

## Histórico

- Editor seguro de INI e SandboxVars com round-trip, validação, conflito e store atômico.
- Controle Linux via systemd, login com PBKDF2 e bloqueio, console FIFO/journal e auditoria.
- RCON para jogadores e whitelist, reinício com avisos e níveis de acesso da Build 42.
- Mods Workshop com SteamCMD isolado, submods, versões, dependências, mapas, opções e traduções.
- Backups/restauração/zeragem, reinícios e updates automáticos com trava antirrepetição, histórico e métricas.
- Cardinal com coleta incremental, ponte Lua, regras protegidas e LLM opcional desligado por padrão.
- Preparação open source: configuração genérica por `server.env`, documentação bilíngue, licença MIT e fixtures sintéticos.

## Pendências

- Validar visualmente a implantação pública e os scripts em um host Debian descartável; não executar instalação a partir deste workspace.
- Windows completo é o próximo passo: serviço, console, logs, backups, SteamCMD.exe e updates equivalentes.
- Migrar para .NET 10 antes do fim do suporte do .NET 8 em novembro de 2026.

## Armadilhas

- `PZ_SERVER_NAME` deve seguir `^[A-Za-z0-9_-]{1,32}$`; scripts leem `/etc/pzmanager/server.env` e o painel deve usar o mesmo `ServerName`.
- `deploy/linux/50-pzmanager.rules` e `pzmanager.service` são templates renderizados pelo instalador; não os copie sem substituir os marcadores.
- Lua do PZ aceita apenas extensões permitidas; a ponte Cardinal usa JSON dentro de arquivos `.txt`.
- Automação nunca pode reiniciar o servidor em loop; toda ação automática precisa de trava contra repetição.
- Testes de interface devem fazer login real por `GET /login` e `POST /auth/login` com antiforgery.
- Nunca use o mesmo caminho para uma página Blazor e um endpoint `MapPost`.
- O painel pode rodar em HTTP na LAN; APIs de navegador que exigem contexto seguro precisam de fallback.
- Scripts estáticos devem ter versão no endereço e chamadas JS feitas pelo C# precisam tratar `JSException`.
- Traduções Sandbox têm aliases e valores internos diferentes dos rótulos; resolva padrão e opções por caminho/rótulo.
- O modal de reconexão deve ficar acima das confirmações; o rascunho usa `sessionStorage` e assinatura do arquivo.
- RCON pode devolver múltiplos pacotes e demorar; autenticação e `help` exigem agregação.
- O jogo reescreve o INI ao iniciar; preserve grupo e `UMask=0007`.
- No Linux, animsets de mods podem exigir nomes minúsculos; `pz-minusculas` cria links antes do start.
- Opções de mods usam o conteúdo sob `<InstallPath>/steamapps/workshop/content/108600`, não o cache do painel.
- Backup, restauração, zeragem e controle compartilham `IServerOperationLock`; restauração valida nome relido do disco.
- RAM/CPU vêm de `systemctl show` porque o serviço pode ocultar `/proc`.
- Só a unit restrita de update altera a instalação do jogo e sempre após backup obrigatório.
- Estado de automação, histórico e métricas são JSON atômico no `DataRoot`; gráficos não usam CDN.
- Cardinal nasce desligado, usa o mesmo validador para ações manuais/regras/LLM e audita bypass de proteção.
- O painel precisa somente de leitura nos logs/Lua e escrita na pasta de troca da Cardinal.
- A regra polkit versionada deve continuar completa para start/stop/restart e units permitidas.
