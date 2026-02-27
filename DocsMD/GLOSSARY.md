# Glossary

Um guia de termos técnicos usados no projeto **NirvanaRemap**. Organizado alfabeticamente para referência rápida.

---

## A

### Analog Input
Entrada contínua que pode ter valores intermediários, como analógicos de controle (0.0 a 1.0 ou -1.0 a 1.0). Contrasta com entrada digital (0 ou 1).

**Exemplos:**
- Posição X do analógico esquerdo: `-0.8` (esquerda)
- Gatilho direito: `0.4` (40% pressionado)

### Anti-Jitter
Filtro para evitar oscilações indesejadas em leituras de eixos analógicos. Implementado através de um valor epsilon (`ChangeEpsilon = 0.003`) que ignora mudanças menores que 0.3%.

**Ver também:** [Deadzone](#deadzone), [Delta Snapshot](#delta-snapshot)

### Avalonia UI
Framework multiplataforma para criação de interfaces gráficas em .NET usando XAML. Permite que a mesma UI funcione em Windows, Linux e macOS.

**No projeto:** Camada de apresentação (`Avalonia/` folder).

**Referência:** https://avaloniaui.net/

---

## B

### Batch Event
Evento que agrupa múltiplas mudanças de input em um único disparo para otimizar performance. No `GamepadRemapService`, inputs são agrupados e emitidos com throttling de 2ms (`MinBatchIntervalMs`).

**Ver também:** [Polling Rate](#polling-rate), [Throttling](#throttling)

### Bounded Channel
Estrutura de dados thread-safe com capacidade limitada. Usado em `RawVirtualizationRunner` para enfileirar snapshots sem consumir memória infinita.

**Configuração no projeto:**
```csharp
new BoundedChannelOptions(capacity: 8)
{
    FullMode = BoundedChannelFullMode.DropOldest
}
```

---

## C

### Calibration
Ajuste de parâmetros de entrada para compensar características físicas do controle, como deadzone, sensibilidade e curvas de resposta.

**Propriedades calibráveis:**
- Deadzone de analógicos
- Sensibilidade (multiplier)
- Inversão de eixos
- Gamma curve

**Ver também:** [Deadzone](#deadzone), [Response Curve](#response-curve)

### Circle-to-Square Mapping
Transformação matemática que converte o movimento circular natural dos analógicos para um espaço quadrado, permitindo alcançar valores máximos nas diagonais.

**Exemplo:**
```csharp
// Antes: diagonal = 0.707 (√2/2)
// Depois: diagonal = 1.0 (máximo)
var (sqX, sqY) = AxisUtils.CircleToSquare(x, y);
```

**Aplicado em:** `ViGEmOutput.ApplyAll()`

### Clean Architecture
Padrão arquitetural que organiza código em camadas concêntricas com dependências apontando para dentro (Core). Garante independência de frameworks externos.

**Camadas no projeto:**
1. **Core** (centro) - Entidades e interfaces
2. **ApplicationLayer** - Lógica de negócio
3. **Infrastructure** - Adaptadores externos
4. **Avalonia** (exterior) - UI

**Ver também:** [Dependency Inversion](#dependency-inversion), [Onion Architecture](#onion-architecture)

---

## D

### Deadzone
Área central do analógico onde inputs são ignorados para compensar drift físico. Expresso como porcentagem do range total.

**Valores no projeto:**
- Analógicos: `10%` (`StickDeadzone = 0.10`)
- Gatilhos: `5%` (`TriggerDeadzone = 0.05`)

**Fórmula aplicada:**
```csharp
if (Math.Abs(value) < StickDeadzone) return 0.0;
var magnitude = (Math.Abs(value) - StickDeadzone) / (1.0 - StickDeadzone);
```

### Delta Snapshot
Snapshot parcial contendo apenas inputs que mudaram desde a última leitura. Reduz tráfego de eventos e processamento.

**No projeto:** `GamepadRemapService.InputBatch` envia deltas, mas `MappingEngine` mantém estado cumulativo.

### Dependency Injection (DI)
Padrão de design que fornece dependências para uma classe ao invés de criá-las internamente. Facilita testes e desacoplamento.

**Container usado:** `Microsoft.Extensions.DependencyInjection`

**Configurado em:** `Program.ConfigureServices()`

### Device Instance Path
Identificador único do Windows para dispositivos de hardware. Formato hierárquico usado pelo HidHide.

**Exemplo:**
```
HID\VID_054C&PID_0CE6&IG_00\7&2a8c91f0&0&0000
```

**Componentes:**
- `HID` - Barramento (HID, USB)
- `VID_054C` - Vendor ID (Sony)
- `PID_0CE6` - Product ID (DualSense)
- `IG_00` - Interface GUID
- Resto - Instance path específico

### Digital Input
Entrada binária que só pode estar ativa (1.0) ou inativa (0.0). Botões são sempre digitais.

**Conversão de gatilhos para digital:**
```csharp
const double threshold = 0.5;
bool pressed = triggerValue >= threshold;
```

**Ver também:** [Analog Input](#analog-input)

### DInput (DirectInput)
API antiga da Microsoft para dispositivos de entrada genéricos. Suporta mais tipos de controles que XInput, mas requer configuração manual.

**Características:**
- Suporta até 128 botões
- Não tem layout padrão
- Usado por controles não-Xbox (Flydigi, joysticks)

**No projeto:** Capturado via SDL3 (API Joystick)

**Ver também:** [XInput](#xinput), [SDL3](#sdl3)

---

## E

### Event-Driven Architecture
Padrão onde componentes comunicam via eventos assíncronos ao invés de chamadas diretas. Desacopla produtor e consumidor.

**Eventos no projeto:**
- `GamepadRemapService.InputBatch` (snapshot disponível)
- `IGamepadService.ConnectionChanged` (conexão alterada)
- `IGamepadService.StateChanged` (estado low-level)

---

## F

### Feedback Loop
Situação onde a saída de um sistema realimenta sua entrada, causando comportamento indesejado. No contexto de remapping, quando o controle virtual é detectado como entrada física.

**Mitigação:** `IsLikelyVirtual()` filtra dispositivos ViGEm (VID 0x045E, PID 0x028E)

---

## G

### Gamma Curve (Response Curve)
Transformação não-linear aplicada a inputs analógicos para ajustar sensibilidade. Valores > 1.0 reduzem sensibilidade em movimentos pequenos.

**No projeto:**
```csharp
const double ResponseGamma = 1.35;
magnitude = Math.Pow(magnitude, ResponseGamma);
```

**Efeito:**
- Gamma < 1.0: Mais sensível (agressivo)
- Gamma = 1.0: Linear
- Gamma > 1.0: Menos sensível (suave)

**Ver também:** [Calibration](#calibration)

---

## H

### Headless Mode
Modo de execução sem interface gráfica. Útil para rodar como serviço em background.

**Ativação:**
```bash
dotnet run --project Avalonia -- --raw
```

**Implementação:** `RawVirtualizationRunner`

### HidHide
Driver do Windows que permite ocultar dispositivos HID de aplicações específicas enquanto os mantém visíveis para outras.

**Uso no projeto:**
1. Adicionar NirvanaRemap à whitelist
2. Habilitar hiding global
3. Adicionar controles físicos à blacklist

**Resultado:** Jogos veem apenas o controle virtual ViGEm.

**Download:** https://github.com/nefarius/HidHide

**Ver também:** [Device Instance Path](#device-instance-path)

---

## I

### Idempotent Operation
Operação que pode ser executada múltiplas vezes com o mesmo efeito da primeira execução.

**Exemplo no projeto:**
```csharp
void EnsureConnected(); // Pode chamar múltiplas vezes
```

### Interface Segregation
Princípio SOLID que sugere interfaces pequenas e específicas ao invés de grandes e genéricas.

**Exemplo:**
- `IGamepadService` (captura)
- `IOutputService` (emissão)
- `IHidHideService` (hiding)

Ao invés de um único `IGamepadManager`.

---

## J

### Joystick API
API de baixo nível do SDL3 para dispositivos genéricos sem mapeamento padrão.

**Usado quando:** Controle não tem perfil SDL_GameController (ex: Flydigi em modo DInput)

**Diferenças do Gamepad API:**
- Sem nomes padronizados (botão 0, 1, 2...)
- Sem garantia de layout
- Mais controles suportados

**Ver também:** [DInput](#dinput-directinput)

---

## K

### Kernel-Mode Driver
Driver que roda com privilégios elevados no núcleo do sistema operacional.

**No projeto:** ViGEm usa driver kernel-mode para emular controles com baixa latência e compatibilidade total.

---

## L

### Latency
Tempo entre ação física no controle e reflexo no jogo. Latência total = captura + processamento + emissão.

**No projeto:**
- SDL polling: ~8ms
- Mapping: <0.1ms
- ViGEm emit: ~1ms
- **Total: ~9-10ms**

**Ver também:** [Polling Rate](#polling-rate)

---

## M

### Mapping Profile
Configuração de mapeamento físico→lógico salva em arquivo JSON.

**Localização:**
```
%APPDATA%/NirvanaRemap/mappings/
```

**Estrutura:**
```json
{
  "ButtonA": "ButtonSouth",
  "ThumbLX": "LeftStickX_Pos"
}
```

**Ver também:** [Physical Input](#physical-input), [Logical Action](#logical-action)

### MVVM (Model-View-ViewModel)
Padrão de design para separar lógica de apresentação (ViewModel) de UI (View).

**No projeto:**
- **View:** XAML files (`MainWindow.axaml`)
- **ViewModel:** Classes com `CommunityToolkit.Mvvm` (`MainViewModel.cs`)
- **Model:** Entidades do Core (`GamepadState.cs`)

---

## N

### Normalization
Conversão de valores raw para um range padrão (geralmente -1.0 a 1.0 ou 0.0 a 1.0).

**Exemplo:**
```csharp
// XInput raw axis: -32768 a 32767
// Normalizado: -1.0 a 1.0
double normalized = rawValue >= 0 
    ? rawValue / 32767.0 
    : rawValue / 32768.0;
```

---

## O

### Onion Architecture
Sinônimo de [Clean Architecture](#clean-architecture). Nome vem da visualização em camadas concêntricas como uma cebola.

### Orchestrator
Classe que coordena múltiplos serviços para realizar uma tarefa complexa.

**Exemplo:** `GamepadVirtualizationOrchestrator` coordena HidHide para ocultar dispositivos físicos.

---

## P

### Physical Input
Entrada bruta do controle físico antes de qualquer mapeamento.

**Exemplos:**
- `ButtonSouth` (A no Xbox, X no PlayStation)
- `LeftStickX_Pos` (movimento direito do analógico esquerdo)

**Ver também:** [Logical Action](#logical-action)

### Polling Rate
Frequência de leitura do estado do controle. Expressa em Hz (vezes por segundo).

**No projeto:**
```csharp
const int PollIntervalMs = 8; // 1000ms / 8ms = 125Hz
```

**Trade-off:**
- Mais Hz: Menor latência, mais CPU
- Menos Hz: Maior latência, menos CPU

### P/Invoke (Platform Invocation)
Mecanismo do .NET para chamar funções nativas (C/C++) de código gerenciado (C#).

**Usado em:**
- XInput interop (`xinput1_4.dll`)
- SDL3 interop (unsafe pointers)

---

## R

### Registry Modification
Alteração do registro do Windows. HidHide armazena configuração lá.

**Chaves modificadas:**
```
HKLM\SYSTEM\CurrentControlSet\Services\HidHide\Parameters
```

**Operações:**
- Adicionar aplicação à whitelist
- Habilitar/desabilitar hiding global
- Gerenciar lista de dispositivos ocultos

### Response Curve
Ver [Gamma Curve](#gamma-curve-response-curve).

---

## S

### SDL3 (Simple DirectMedia Layer 3)
Biblioteca cross-platform para captura de input, áudio e vídeo de baixo nível.

**Usado no projeto:** Captura de gamepad/joystick via API C nativa.

**Binding C#:** `ppy.SDL3-CS`

**Vantagens:**
- Cross-platform
- Baixa latência
- Suporta XInput e DInput
- Eventos de background

**Referência:** https://wiki.libsdl.org/SDL3/

### Service Locator
Anti-padrão onde classes requisitam dependências de um registro global. Evitado em favor de [Dependency Injection](#dependency-injection-di).

### Singleton
Padrão onde apenas uma instância de uma classe existe no sistema.

**No projeto:** Serviços registrados como singletons:
```csharp
services.AddSingleton<IOutputService, ViGEmOutput>();
```

**Razão:** Controles virtuais e físicos mantêm estado (handles, conexões).

### Snapshot
Captura instantânea do estado completo ou parcial do controle.

**Formato:**
```csharp
Dictionary<string, double> snapshot = new()
{
    ["LX"] = 0.8,
    ["A"] = 1.0
};
```

**Ver também:** [Delta Snapshot](#delta-snapshot)

---

## T

### Throttling
Limitação da frequência de execução de uma operação. Previne sobrecarga.

**No projeto:**
```csharp
const int MinBatchIntervalMs = 2;
if (now - lastBatchTime < MinBatchIntervalMs) return;
```

**Efeito:** InputBatch não dispara mais que 500 vezes/segundo.

### Trigger
Botão analógico (L2/R2, LT/RT) que retorna valor de 0.0 a 1.0 baseado em quão pressionado está.

**Conversão raw → normalizado:**
```csharp
// XInput: 0-255
// Normalizado: 0.0-1.0
double normalized = rawTrigger / 255.0;
```

---

## U

### Unsafe Code
Código C# que usa ponteiros e memória não-gerenciada. Requer `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.

**No projeto:** SDL3 bindings usam ponteiros:
```csharp
unsafe void OpenFirstPad()
{
    SDL_Gamepad* pad = SDL_OpenGamepad(jid);
    // ...
}
```

**Ver também:** [P/Invoke](#pinvoke-platform-invocation)

---

## V

### ViGEm (Virtual Gamepad Emulation)
Driver kernel-mode que permite criar controles virtuais Xbox 360/DualShock 4 reconhecidos como genuínos pelo Windows.

**Client library:** `Nefarius.ViGEm.Client`

**Usado no projeto:** `ViGEmOutput` implementa `IOutputService`

**Vantagens:**
- Compatibilidade universal com jogos
- Baixa latência (kernel-mode)
- Múltiplos controles virtuais simultâneos

**Download:** https://github.com/nefarius/ViGEmBus

### Virtual Controller
Dispositivo de entrada emulado por software que o sistema operacional reconhece como hardware real.

**No projeto:** Xbox 360 controller via ViGEm.

**Ver também:** [ViGEm](#vigem-virtual-gamepad-emulation)

---

## W

### Whitelist
Lista de aplicações permitidas. No contexto de HidHide, aplicações whitelistadas podem ver dispositivos ocultos.

**Exemplo:** NirvanaRemap.exe na whitelist vê o controle físico, mas jogos não.

---

## X

### XInput
API da Microsoft para controles compatíveis com Xbox (360/One/Series). Oferece interface padronizada e simples.

**Características:**
- Máximo 4 controles simultâneos
- Layout fixo (A/B/X/Y, analógicos, gatilhos)
- Vibração
- Bateria (em wireless)

**Limitações:**
- Apenas controles Xbox-like
- Sem suporte a touchpad, gyro, luzes

**Ver também:** [DInput](#dinput-directinput)

---

## Y

### Y-Axis Inversion
Inversão do eixo vertical dos analógicos. SDL usa +Y = baixo, XInput usa +Y = cima.

**Correção aplicada:**
```csharp
controller.SetAxisValue(Xbox360Axis.LeftThumbY, AxisToShort(-normalizedLY));
```

---

## Z

### Zero State
Estado padrão onde todos inputs estão em posição neutra.

**Valores:**
- Botões: `0.0` (não pressionados)
- Analógicos: `0.0` (centrados)
- Gatilhos: `0.0` (soltos)

**Criado por:** `MappingEngine.InitOutputState()`

---

## Símbolos e Acrônimos

### API
**Application Programming Interface** - Interface de programação de aplicação.

### CLI
**Command-Line Interface** - Interface de linha de comando.

### CPU
**Central Processing Unit** - Unidade central de processamento.

### DI
Ver [Dependency Injection](#dependency-injection-di).

### DLL
**Dynamic-Link Library** - Biblioteca de vínculo dinâmico (Windows).

### GUI
**Graphical User Interface** - Interface gráfica do usuário.

### HID
**Human Interface Device** - Dispositivo de interface humana (teclados, mouses, controles).

### JSON
**JavaScript Object Notation** - Formato de dados baseado em texto.

### MVVM
Ver [MVVM](#mvvm-model-view-viewmodel).

### PID
**Product ID** - Identificador de produto USB.

### SDK
**Software Development Kit** - Kit de desenvolvimento de software.

### UI
**User Interface** - Interface do usuário.

### USB
**Universal Serial Bus** - Barramento serial universal.

### VID
**Vendor ID** - Identificador de fabricante USB.

### XAML
**eXtensible Application Markup Language** - Linguagem de marcação para UI.

---

## Referências Externas

Para aprofundamento, consulte:

- **Clean Architecture:** [The Clean Architecture (Uncle Bob)](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- **MVVM Pattern:** [Microsoft MVVM Docs](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm)
- **SDL3:** [SDL3 Wiki](https://wiki.libsdl.org/SDL3/)
- **ViGEm:** [ViGEm Documentation](https://vigem.org/)
- **XInput:** [XInput Reference](https://learn.microsoft.com/en-us/windows/win32/xinput/xinput-game-controller-apis-portal)

---

## Contribuindo para o Glossário

Encontrou um termo confuso não documentado? Abra uma issue ou pull request! Ver [CONTRIBUTING.md](CONTRIBUTING.md) para detalhes.
