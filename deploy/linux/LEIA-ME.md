# Implantação no Debian

A documentação pública de instalação foi movida para:

- [Preparação genérica do Debian (pt-BR)](../../docs/debian-setup.pt-BR.md)
- [Início rápido e configuração](../../README.pt-BR.md#início-rápido-debian-13)

Antes de executar os scripts, copie `server.env` para `/etc/pzmanager/server.env` e ajuste o nome e a raiz do servidor. O instalador renderiza `50-pzmanager.rules` com o nome validado; não copie o template diretamente para o diretório do polkit.

