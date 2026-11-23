using System.Runtime.CompilerServices;
using Prometheus;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// Встроенный адаптер prometheus-net по умолчанию публикует все Meter процесса и дублировал бы счётчики моста; в сервисе он ограничен списком
/// встроенных источников (MetricsConfiguration). В тестах он выключен совсем — и до первого обращения к Metrics из любого теста: настройка,
/// сделанная позже, уже не действует, а порядок тестов не задан.
/// </summary>
internal static class MetricsTestSetup
{
#pragma warning disable CA2255 // инициализатор модуля нужен именно здесь: раньше любого теста сборки
    [ModuleInitializer]
    internal static void DisableMeterAdapter() => Metrics.ConfigureMeterAdapter(options => options.InstrumentFilterPredicate = _ => false);
#pragma warning restore CA2255
}
