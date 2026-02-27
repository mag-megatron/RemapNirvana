# Guia: Criando o Instalador do NirvanaRemap com InnoSetup

Este guia descreve o passo a passo completo para gerar um instalador `.exe` do **NirvanaRemap** utilizando o [Inno Setup](https://jrsoftware.org/isinfo.php).

---

## Pré-requisitos

| Item | Detalhes |
|------|----------|
| **Inno Setup** | v6.x — [Download](https://jrsoftware.org/isdl.php) |
| **.NET 9 SDK** | Instalado e disponível no `PATH` |
| **Publish do projeto** | Build self-contained gerado (instruções abaixo) |

---

## Passo 1 — Publicar o projeto (Self-Contained)

Abra um terminal na raiz do repositório (`RemapNirvana/`) e execute:

```powershell
dotnet publish Avalonia/AvaloniaUI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o ./publish
```

> **Por que self-contained?** A máquina de destino **não** precisará ter o .NET 9 instalado. Todas as DLLs do runtime e das dependências (SDL3, ViGEm, Avalonia, etc.) serão incluídas na pasta `publish/`.

Após o comando, a pasta `publish/` conterá todos os arquivos necessários para rodar a aplicação.

### Verificação rápida

```powershell
# Teste se o executável funciona antes de empacotar
.\publish\NirvanaRemap.exe
```

---

## Passo 2 — Instalar o Inno Setup

1. Baixe o instalador em: https://jrsoftware.org/isdl.php
2. Execute o instalador e siga o wizard (padrão).
3. Após a instalação, abra o **Inno Setup Compiler** a partir do menu Iniciar.

---

## Passo 3 — Criar o script `.iss`

Crie o arquivo `installer.iss` na raiz do repositório com o conteúdo abaixo. Ele já está configurado para o **NirvanaRemap**:

```iss
; ==========================================================
; NirvanaRemap - Inno Setup Script
; ==========================================================

#define MyAppName      "NirvanaRemap"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "RemapNirvana Team"
#define MyAppURL       ""
#define MyAppExeName   "NirvanaRemap.exe"

[Setup]
AppId={{YOUR-GUID-AQUI}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.\installer_output
OutputBaseFilename=NirvanaRemap_Setup_{#MyAppVersion}
SetupIconFile=Avalonia\Assets\avalonia-logo.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english";             MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Inclui TUDO da pasta publish (exe, dlls, runtimes, etc.)
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";         Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}";   Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Executar {#MyAppName}"; Flags: nowait postinstall skipifsilent
```

### ⚠️ Ação necessária: Gere um GUID único

Substitua `{YOUR-GUID-AQUI}` por um GUID real. Para gerar:

```powershell
[guid]::NewGuid().ToString()
# Exemplo de saída: a1b2c3d4-e5f6-7890-abcd-ef1234567890
```

Cole o resultado no campo `AppId`, mantendo as chaves duplas no início:
```
AppId={{a1b2c3d4-e5f6-7890-abcd-ef1234567890}
```

---

## Passo 4 — Compilar o instalador

### Opção A: Via interface gráfica

1. Abra o **Inno Setup Compiler**
2. `File → Open` → selecione `installer.iss`
3. `Build → Compile` (ou **Ctrl+F9**)
4. O instalador será gerado em `installer_output/NirvanaRemap_Setup_1.0.0.exe`

### Opção B: Via linha de comando

```powershell
# Ajuste o caminho do ISCC.exe se necessário
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
```

---

## Passo 5 — Testar o instalador

1. Execute `installer_output/NirvanaRemap_Setup_1.0.0.exe`
2. Verifique se:
   - O programa é instalado em `C:\Program Files\NirvanaRemap\`
   - O atalho aparece no Menu Iniciar
   - O ícone de desktop aparece (se marcado)
   - O programa abre corretamente após a instalação
   - A desinstalação funciona via **Configurações → Aplicativos**

---

## Passo 6 (Opcional) — Incluir pré-requisito do ViGEmBus

Se o ViGEmBus Driver precisar estar instalado na máquina de destino, adicione uma etapa de verificação/instalação ao script. Coloque o instalador do ViGEmBus (`.exe` ou `.msi`) em uma pasta `redist/`:

```iss
[Files]
Source: "redist\ViGEmBusSetup_x64.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; Instala o ViGEmBus silenciosamente antes de abrir o app
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\ViGEmBusSetup_x64.msi"" /quiet /norestart"; \
    StatusMsg: "Instalando driver ViGEmBus..."; Flags: waituntilterminated
```

> **Nota:** O ViGEmBus pode ser baixado em https://github.com/nefarius/ViGEmBus/releases

---

## Resumo do fluxo completo

```
1. dotnet publish  →  pasta publish/ com todos os binários
2. Criar installer.iss  →  script com configurações do instalador
3. ISCC.exe installer.iss  →  gera NirvanaRemap_Setup_1.0.0.exe
4. Distribuir o .exe do instalador para as máquinas de destino
```

---

## Estrutura de arquivos esperada

```
RemapNirvana/
├── installer.iss                         ← script do Inno Setup
├── publish/                              ← saída do dotnet publish
│   ├── NirvanaRemap.exe
│   ├── NirvanaRemap.dll
│   ├── SDL3.dll
│   ├── ... (todas as dependências)
│   └── runtimes/
├── installer_output/                     ← (gerada pelo InnoSetup)
│   └── NirvanaRemap_Setup_1.0.0.exe
├── redist/                               ← (opcional) drivers
│   └── ViGEmBusSetup_x64.msi
└── Avalonia/
    └── Assets/
        └── avalonia-logo.ico
```
