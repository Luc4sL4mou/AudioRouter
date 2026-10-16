# AudioRouter

Aplicativo de bandeja para Windows 11 que controla o áudio **por aplicativo**: volume, mudo, peak meter em tempo real e,
principalmente, **escolha do dispositivo de saída de cada app** (por exemplo, Brave nos alto-falantes e o jogo no headset).
As escolhas são lembradas e reaplicadas automaticamente.

- Clique esquerdo no ícone da bandeja abre um flyout de 360 px perto da bandeja (acrílico, tema claro/escuro do sistema, fonte Segoe UI Variable). Ele fecha ao clicar fora ou com Esc.
- Topo do flyout: dispositivo de saída padrão (troca rápida), volume mestre e mudo.
- Uma linha por app com sessão de áudio: ícone, nome, volume, mudo, peak meter e seletor de dispositivo.
- As rotas são salvas em JSON por nome de executável e reaplicadas ao abrir o app e ao conectar/desconectar dispositivos.
- Menu de contexto da bandeja (clique direito): **Iniciar com o Windows** e **Sair**.

## Requisitos

| Para | Precisa de |
|---|---|
| Executar | Windows 11 (desenvolvido e testado no build 26300). O acrílico exige o build 22621 ou superior; em builds anteriores a janela usa um fundo opaco. O Windows 10 não foi testado. |
| Compilar | [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (testado com 10.0.402) |
| Executar sem publicar autocontido | .NET 10 **Desktop Runtime** (a variante "dependente de framework" abaixo) |

## Estrutura

```
AudioRouter/
├── src/
│   ├── AudioRouter.Core/     Lógica de áudio e configurações, sem UI
│   │   ├── Audio/            Enumeração, snapshot, roteamento por app, reaplicação
│   │   └── Settings/         JSON em %AppData% e "Iniciar com o Windows"
│   └── AudioRouter.App/      WPF: bandeja, flyout, view-models
│       └── Assets/           Ícone oficial (icon-source.png) e o .ico gerado a partir dele
├── tests/
│   └── AudioRouter.Probe/    Console de diagnóstico (ver "Probe" abaixo)
└── tools/
    └── make-icon.ps1         Gera o AudioRouter.ico a partir da imagem oficial
```

## Ícone

O ícone oficial fica em `src/AudioRouter.App/Assets/icon-source.png`. O `AudioRouter.ico` (16, 20, 24, 32, 40, 48, 64, 128 e 256 px)
é gerado a partir dele e usado no `.exe` (Explorer e atalhos), na bandeja e na janela. Para trocar o ícone, substitua o PNG e rode:

```powershell
.\tools\make-icon.ps1
```

O script recorta o tile e remove o halo quase invisível das bordas da imagem, que viraria ruído em tamanhos pequenos.
Depois, recompile e publique de novo. Se um atalho existente continuar mostrando o ícone antigo, rode `ie4uinit.exe -show` para renovar o cache de ícones do Windows.

## Compilar e executar

Na pasta raiz do repositório:

```powershell
dotnet build AudioRouter.slnx -c Release     # compila Core, App e Probe
dotnet run --project src\AudioRouter.App
```

A solução `AudioRouter.slnx` também abre no Visual Studio 2022 17.13 ou superior.

O app inicia **escondido na bandeja** (o ícone pode ficar na área de ícones ocultos, atrás da seta `^`).
Só existe uma instância por vez: abrir o app de novo com ele já rodando encerra a segunda instância.

Opções de linha de comando (úteis em desenvolvimento):

| Opção | Efeito |
|---|---|
| `--show` | Abre o flyout ao iniciar |
| `--keep-open` | O flyout não fecha ao perder o foco |
| `--theme=light` / `--theme=dark` | Ignora o tema do Windows |

Exemplo: `dotnet run --project src\AudioRouter.App -- --show --keep-open --theme=dark`

## Gerar o .exe (`dotnet publish`)

Todos os comandos geram um único arquivo `AudioRouter.App.exe` na pasta `publish\`.

### Opção 1: autocontido (recomendada para distribuir)

Roda em qualquer Windows 10/11 x64, **sem instalar o .NET**. Tamanho: ~72 MB.

```powershell
dotnet publish src\AudioRouter.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

### Opção 2: dependente de framework (arquivo pequeno)

Tamanho: <1 MB, mas exige o **.NET 10 Desktop Runtime** instalado na máquina que vai rodar.

```powershell
dotnet publish src\AudioRouter.App -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=none -o publish
```

Resultado: `publish\AudioRouter.App.exe`.

Observações:

- Para ARM64, troque `win-x64` por `win-arm64`.
- Sem `EnableCompressionInSingleFile` o autocontido fica com ~165 MB.
- Não use trimming (`PublishTrimmed`): o WPF não o suporta.
- Na primeira execução do autocontido o Windows extrai bibliotecas nativas para uma pasta temporária, então a abertura inicial demora alguns segundos a mais.
- O executável não é assinado, então o SmartScreen pode avisar na primeira vez em outra máquina ("Mais informações" > "Executar assim mesmo").
- Se o app estiver rodando, feche-o pela bandeja antes de publicar de novo (o exe fica em uso).

## Onde ficam os dados

- **Configurações:** `%AppData%\AudioRouter\settings.json`, criado só quando algo é salvo. Exemplo:

  ```json
  {
    "Version": 1,
    "StartWithWindows": false,
    "Routes": {
      "brave.exe": {
        "DeviceId": "{0.0.0.00000000}.{243c4238-bd50-4ac5-aeb0-90484afdda28}",
        "DeviceName": "Alto-falantes (High Definition Audio Device)"
      }
    }
  }
  ```

  Se o arquivo estiver corrompido, ele é renomeado para `settings.json.bad` e o app recomeça com configurações vazias.
- **Iniciar com o Windows:** o app escreve `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (valor `AudioRouter`) apontando para o executável atual. Não precisa de administrador. Se você mover o exe, o caminho é corrigido na próxima abertura.

Para desfazer tudo: desmarque "Iniciar com o Windows", feche o app e apague `%AppData%\AudioRouter`.

## Como funciona (resumo)

- **Volume, mudo e peak:** Core Audio via [NAudio](https://github.com/naudio/NAudio).
- **Roteamento por app:** `IAudioPolicyConfigFactory`, a API interna que a página *Configurações > Som > Mixer de volume* do Windows usa. O ID do dispositivo passado a ela tem o formato `\\?\SWD#MMDEVAPI#{...}#{e6327cad-dcec-4949-ae8a-991e976a79d2}`.
- **Dispositivo padrão:** `IPolicyConfig` (também não documentada, estável há anos).
- **Reaplicação das rotas:** notificações de dispositivo do Core Audio, mais uma verificação a cada 3 s para apps que começam a tocar depois. Em repouso o app usa cerca de 0,2% de um núcleo.

## Limitações conhecidas

- **APIs não documentadas.** O roteamento por app e a troca do dispositivo padrão dependem de interfaces internas do Windows. Podem mudar em versões futuras; o código trata falhas sem derrubar o app, mas o recurso pode parar de funcionar.
- **O AudioRouter é a fonte da verdade das rotas que ele salvou.** Se você mudar a saída de um desses apps em *Configurações > Som*, o AudioRouter a desfaz em até 3 s. Apps que você nunca configurou por ele não são tocados. Para parar de forçar uma rota, escolha "Padrão do sistema" na linha do app.
- **Só aparecem apps que já abriram uma sessão de áudio** (já tocaram algum som). Apps ociosos ficam na lista esmaecidos.
- **O app pode precisar reabrir o stream** (pausar e tocar de novo) para migrar de dispositivo; o Brave migra sozinho, outros apps podem não migrar.
- Apps com vários processos (navegadores) aparecem como **uma linha só**, agrupados pelo nome do executável.
- "Sons do sistema" não aparece na lista (não é possível rotear por app).
- Apps elevados (executados como administrador) podem não ter o caminho/ícone lido.
- Dispositivos de **entrada** (microfones) não são tratados; só saída.

## Probe (diagnóstico)

`tests/AudioRouter.Probe` é um console para inspecionar o áudio sem a UI:

```powershell
dotnet run --project tests\AudioRouter.Probe                       # lista dispositivos e sessões
dotnet run --project tests\AudioRouter.Probe -- get brave          # rota atual de um app
dotnet run --project tests\AudioRouter.Probe -- route brave "Alto" # roteia para o dispositivo cujo nome contém "Alto"
dotnet run --project tests\AudioRouter.Probe -- clear brave        # remove a rota do Windows para o app
dotnet run --project tests\AudioRouter.Probe -- bench              # mede o custo de captura + reaplicação
```

`route` e `clear` alteram a configuração do Windows (a mesma do Mixer de volume).

## Solução de problemas

| Sintoma | O que fazer |
|---|---|
| Não vejo o ícone | Procure atrás da seta `^` na bandeja; em *Configurações > Personalização > Barra de tarefas > Outros ícones da bandeja* ative o AudioRouter para fixá-lo. |
| O app não abre / fecha na hora | Provavelmente já há uma instância rodando (veja `Get-Process AudioRouter.App`). |
| `dotnet publish` falha com "arquivo em uso" | Feche o app pela bandeja (ou `Stop-Process -Name AudioRouter.App`) e repita. |
| O exe da Opção 2 não abre | Falta o .NET 10 Desktop Runtime; instale-o ou use a Opção 1. |
| A rota some e volta | Veja "O AudioRouter é a fonte da verdade" em *Limitações*. |
