# Preparando um host Debian 13

Este guia prepara um host genérico para o PZ Server Manager. Revise cada comando para o seu ambiente.

## Conta de serviço e diretórios

```bash
sudo useradd --system --create-home --home-dir /opt/pzserver --shell /usr/sbin/nologin pzserver
sudo install -d -o pzserver -g pzserver -m 750 /opt/pzserver
sudo install -d -o root -g root -m 755 /etc/pzmanager
sudo install -o root -g root -m 644 deploy/linux/server.env /etc/pzmanager/server.env
sudoedit /etc/pzmanager/server.env
```

`PZ_SERVER_NAME` deve seguir `^[A-Za-z0-9_-]{1,32}$`. Mantenha `PZ_ROOT` em um sistema de arquivos local com espaço para o jogo, conteúdo da Workshop e backups.

## SteamCMD e servidor dedicado

Instale a dependência de 32 bits e o SteamCMD:

```bash
sudo dpkg --add-architecture i386
sudo apt update
sudo apt install -y ca-certificates curl lib32gcc-s1 ufw polkitd
sudo -u pzserver mkdir -p /opt/pzserver/steamcmd /opt/pzserver/pz
sudo -u pzserver bash -c 'curl -fsSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar -xz -C /opt/pzserver/steamcmd'
sudo -u pzserver /opt/pzserver/steamcmd/steamcmd.sh \
  +force_install_dir /opt/pzserver/pz +login anonymous \
  +app_update 380870 validate +quit
```

Se `PZ_ROOT` for diferente, substitua `/opt/pzserver` de forma consistente.

Inicie o Dedicated Server uma vez com `-servername <PZ_SERVER_NAME>` para criar os arquivos e pare-o antes de rodar `pz-parte2.sh`.

## Firewall

Restrinja SSH e o painel à sua rede administrativa. O exemplo usa a rede de documentação `192.168.1.0/24`:

```bash
ADMIN_CIDR=192.168.1.0/24
sudo ufw default deny incoming
sudo ufw allow from "$ADMIN_CIDR" to any port 22 proto tcp comment 'Administração SSH'
sudo ufw allow from "$ADMIN_CIDR" to any port 8085 proto tcp comment 'PZ Server Manager'
sudo ufw allow 16261:16262/udp comment 'Project Zomboid'
sudo ufw enable
```

O Project Zomboid pode exigir portas UDP adicionais quando o número de jogadores supera a alocação padrão. Não exponha o RCON; mantenha-o em `127.0.0.1`.

## Uso em notebook

Se o host for um notebook, considere desativar a suspensão ao fechar a tampa enquanto estiver ligado à tomada. Edite `/etc/systemd/logind.conf`, defina `HandleLidSwitchExternalPower=ignore` e reinicie `systemd-logind`. Verifique refrigeração, comportamento após falta de energia e saúde do disco antes de deixá-lo sem supervisão.

Continue pelo [início rápido](../README.pt-BR.md#início-rápido-debian-13).
