#!/bin/bash
# Instala backups, restauração e zeragem como serviços acionáveis pelo painel.
set -euo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "$0")" && pwd)
ENV_FILE=/etc/pzmanager/server.env
install -d -o root -g root -m 755 /etc/pzmanager
if [[ ! -f "$ENV_FILE" ]]; then
  install -o root -g root -m 644 "$SCRIPT_DIR/../server.env" "$ENV_FILE"
fi
# shellcheck disable=SC1091
. "$ENV_FILE"
: "${PZ_SERVER_NAME:=servertest}"
: "${PZ_ROOT:=/opt/pzserver}"
[[ "$PZ_SERVER_NAME" =~ ^[A-Za-z0-9_-]{1,32}$ ]] || { echo "PZ_SERVER_NAME inválido"; exit 2; }
[[ "$PZ_ROOT" == /* && "$PZ_ROOT" != / && "$PZ_ROOT" != *..* ]] || { echo "PZ_ROOT inválido"; exit 2; }

install -d -o pzserver -g pzserver -m 2750 "$PZ_ROOT/backups"

cat > /usr/local/sbin/pz-backup <<'SCRIPT'
#!/bin/bash
set -euo pipefail
ENV_FILE=/etc/pzmanager/server.env
[[ -r "$ENV_FILE" ]] && . "$ENV_FILE"
: "${PZ_SERVER_NAME:=servertest}"
: "${PZ_ROOT:=/opt/pzserver}"
[[ "$PZ_SERVER_NAME" =~ ^[A-Za-z0-9_-]{1,32}$ ]] || { echo "PZ_SERVER_NAME inválido"; exit 2; }
[[ "$PZ_ROOT" == /* && "$PZ_ROOT" != / && "$PZ_ROOT" != *..* ]] || { echo "PZ_ROOT inválido"; exit 2; }
Z="$PZ_ROOT/Zomboid"
DEST="$PZ_ROOT/backups"
MOTIVO=$(echo "${1:-manual}" | tr -cd 'a-z0-9-' | cut -c1-20)
STAMP=$(date +%Y%m%d-%H%M%S)
ARQ="$DEST/$PZ_SERVER_NAME-$STAMP-${MOTIVO:-manual}.tar.gz"
if systemctl is-active -q pzserver.service; then
  pz-console save && sleep 15 || true
fi
tar -czf "$ARQ.tmp" -C "$Z" "Saves/Multiplayer/$PZ_SERVER_NAME" Server db
mv "$ARQ.tmp" "$ARQ"
chown pzserver:pzserver "$ARQ"
chmod 640 "$ARQ"
find "$DEST" -maxdepth 1 -type f -name "$PZ_SERVER_NAME-*.tar.gz" -printf '%T@ %p\n' |
  sort -nr | tail -n +15 | cut -d' ' -f2- | xargs -r rm -f --
echo "backup criado: $ARQ ($(du -h "$ARQ" | cut -f1))"
SCRIPT

cat > /usr/local/sbin/pz-restaurar <<'SCRIPT'
#!/bin/bash
set -euo pipefail
ENV_FILE=/etc/pzmanager/server.env
[[ -r "$ENV_FILE" ]] && . "$ENV_FILE"
: "${PZ_SERVER_NAME:=servertest}"
: "${PZ_ROOT:=/opt/pzserver}"
[[ "$PZ_SERVER_NAME" =~ ^[A-Za-z0-9_-]{1,32}$ ]] || { echo "PZ_SERVER_NAME inválido"; exit 2; }
[[ "$PZ_ROOT" == /* && "$PZ_ROOT" != / && "$PZ_ROOT" != *..* ]] || { echo "PZ_ROOT inválido"; exit 2; }
NOME=${1:-}
DEST="$PZ_ROOT/backups"
Z="$PZ_ROOT/Zomboid"
[[ "$NOME" =~ ^${PZ_SERVER_NAME}-[0-9]{8}-[0-9]{6}-[a-z0-9-]{1,20}\.tar\.gz$ ]] || { echo "nome de backup inválido: $NOME"; exit 2; }
[[ -f "$DEST/$NOME" ]] || { echo "backup não existe: $NOME"; exit 2; }
if tar -tzf "$DEST/$NOME" | grep -qv -E "^(Saves/Multiplayer/${PZ_SERVER_NAME}|Server|db)(/|$)"; then
  echo "backup com conteúdo inesperado"
  exit 2
fi
pz-console 'servermsg "O servidor vai restaurar um backup e reiniciar agora."' 2>/dev/null || true
pz-backup antes-restaurar
systemctl stop pzserver.service
rm -rf -- "$Z/Saves/Multiplayer/$PZ_SERVER_NAME"
tar -xzf "$DEST/$NOME" -C "$Z"
chown -R pzserver:pzserver "$Z/Saves/Multiplayer/$PZ_SERVER_NAME" "$Z/Server" "$Z/db"
chmod 660 "$Z/Server/${PZ_SERVER_NAME}"* 2>/dev/null || true
systemctl start pzserver.service
echo "restaurado: $NOME"
SCRIPT
chmod 755 /usr/local/sbin/pz-backup /usr/local/sbin/pz-restaurar

cat > /etc/systemd/system/pz-backup.service <<'UNIT'
[Unit]
Description=Backup do mundo do Project Zomboid
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/pz-backup manual
UNIT
cat > /etc/systemd/system/pz-backup@.service <<'UNIT'
[Unit]
Description=Backup do mundo do Project Zomboid (%I)
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/pz-backup %i
UNIT
cat > /etc/systemd/system/pz-backup-diario.service <<'UNIT'
[Unit]
Description=Backup diário do mundo do Project Zomboid
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/pz-backup diario
UNIT
cat > /etc/systemd/system/pz-backup-diario.timer <<'UNIT'
[Unit]
Description=Backup diário do Project Zomboid (05:00)
[Timer]
OnCalendar=*-*-* 05:00:00
Persistent=true
[Install]
WantedBy=timers.target
UNIT
cat > /etc/systemd/system/pz-restaurar@.service <<'UNIT'
[Unit]
Description=Restaurar backup do Project Zomboid (%I)
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/pz-restaurar %i
UNIT
cat > /etc/systemd/system/pz-zerar-mundo.service <<'UNIT'
[Unit]
Description=Zerar o mundo do Project Zomboid (mantém configs, mods e contas)
[Service]
Type=oneshot
ExecStartPre=/usr/local/sbin/pz-backup antes-zerar
ExecStart=/usr/local/sbin/pz-zerar-mundo --sim
UNIT

sed "s/__PZ_SERVER_NAME__/$PZ_SERVER_NAME/g" "$SCRIPT_DIR/../50-pzmanager.rules" \
  > /etc/polkit-1/rules.d/50-pzmanager.rules
chmod 644 /etc/polkit-1/rules.d/50-pzmanager.rules
systemctl daemon-reload
systemctl restart polkit.service
systemctl enable -q --now pz-backup-diario.timer
echo "== teste: backup manual pelo painel (como pzmanager)"
sudo -u pzmanager systemctl start pz-backup.service && journalctl -u pz-backup.service -n 2 -o cat
sudo -u pzmanager ls "$PZ_ROOT/backups" >/dev/null && echo "painel consegue listar os backups"
echo "== teste: painel NÃO pode iniciar unidades fora da lista"
sudo -u pzmanager systemctl start ssh.service 2>&1 | head -1
sudo -u pzmanager systemctl start "pz-restaurar@..-..-etc-passwd.service" 2>&1 | head -1
