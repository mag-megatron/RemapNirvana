# Relatório de Latência e Eficiência - Fase 1 (Baseline)

## 1. Resumo Executivo
A análise realizada em **12/02/2026** com a versão *Headless* (`--raw`) do NirvanaRemap demonstrou uma eficiência excepcional no processamento de entradas. 
O tempo médio de processamento interno (Input Capture -> Mapping -> Output Emission) foi de apenas **0.1873 ms**, superando largamente a meta inicial de < 0.5 ms.

## 2. Metodologia
- **Modo de Teste**: `Headless (--raw)`
- **Ferramenta de Coleta**: Telemetria interna via `Stopwatch` (High Resolution)
- **Amostragem**: 930 quadros (frames) de processamento contínuo
- **Hardware**: Ambiente de desenvolvimento padrão (Windows 10/11)

## 3. Resultados Detalhados

| Métrica | Valor Obtido | Meta (KPI) | Status |
|:--------|:-------------|:-----------|:-------|
| **Latência Média** | **0.1873 ms** | < 0.5 ms | ✅ Aprovado |
| **Latência Mediana (P50)** | **0.1441 ms** | N/A | Informativo |
| **Latência P95** | **0.2240 ms** | < 1.0 ms | ✅ Aprovado |
| **Latência P99** | **0.5247 ms** | < 2.0 ms | ✅ Aprovado |
| **Jitter (Desvio Padrão)** | **0.6468 ms** | < 1.0 ms | ✅ Aprovado |
| **Pico Máximo (Max)** | 17.8183 ms* | N/A | *Transiente de inicialização* |

### Observações:
- O pico máximo de ~17ms ocorreu apenas durante a inicialização do loop (JIT compilation / warmup).
- 99% das amostras ficaram abaixo de **0.53 ms**, confirmando a estabilidade do sistema.
- A consistência do loop (Jitter) está dentro dos limites aceitáveis para jogos competitivos.

## 4. Conclusão
O NirvanaRemap introduz um **overhead imperceptível** (< 0.2ms em média) na cadeia de input. 
Para fins práticos, o software é "transparente" em termos de latência adicionada.

---
*Gerado automaticamente a partir de `latency_stats.csv`.*
