using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PzServerManager.Application.Abstractions;
using PzServerManager.Infrastructure.Configuration;
using PzServerManager.Infrastructure.Authentication;
using PzServerManager.Infrastructure.ManagedServer;
using PzServerManager.Infrastructure.Persistence;
using PzServerManager.Infrastructure.Processes;
using PzServerManager.Infrastructure.Steam;
using PzServerManager.Infrastructure.Rcon;
using PzServerManager.Infrastructure.Whitelist;
using PzServerManager.Application.Rcon;
using PzServerManager.Application.Configuration;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Application.Mods;
using PzServerManager.Infrastructure.Automation;
using PzServerManager.Infrastructure.Metrics;
using PzServerManager.Infrastructure.Updates;
using PzServerManager.Infrastructure.Cardinal;
using PzServerManager.Application.Backups;
using Microsoft.Extensions.Options;

namespace PzServerManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPzManagerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ManagerOptions>()
            .Bind(configuration.GetSection(ManagerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<ManagerOptions>, ManagerOptionsValidator>();
        services.AddSingleton<IManagerPaths, ManagerPaths>();
        services.AddSingleton<IServerIniStore, ServerIniFileStore>();
        services.AddSingleton<ISandboxVarsStore, SandboxVarsFileStore>();
        services.AddSingleton<UserFileStore>();
        services.AddSingleton<LoginAttemptLimiter>();
        services.AddSingleton<ICommandRunner, CommandRunner>();
        services.AddSingleton<IAuditLog, AuditLog>();
        services.AddSingleton<IServerOperationLock, ServerOperationLock>();
        services.AddSingleton<IOperationDelay, SystemOperationDelay>();
        services.AddSingleton<IWorkshopDownloader, SteamWorkshopDownloader>();
        services.AddSingleton<IWorkshopMetadataClient, SteamWorkshopMetadataClient>();
        services.AddSingleton<WorkshopItemInspector>();
        services.AddSingleton<IAutomationSettingsStore, AutomationSettingsStore>();

        if (configuration.GetSection($"{ManagerOptions.SectionName}:ManagedServer").Exists())
        {
            services.AddSingleton<IServerController, SystemdServerController>();
            services.AddSingleton(provider => new BackupFileNameParser(
                provider.GetRequiredService<IOptions<ManagerOptions>>().Value.ManagedServer!.ServerName));
            services.AddSingleton<IBackupManager, SystemdBackupManager>();
            services.AddSingleton<IServerLogStream, JournalctlServerLogStream>();
            services.AddSingleton<IRconSettingsProvider, ServerIniRconSettingsProvider>();
            services.AddSingleton<IRconClient, RconClient>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IServerCommandReadiness, ServerCommandReadiness>();
            services.AddSingleton<IServerConsole, FifoServerConsole>();
            services.AddSingleton<IWhitelistReader, SqliteWhitelistReader>();
            services.AddSingleton<ServerAdministrationService>();
            services.AddScoped<ServerSettingsService>();
            services.AddScoped<SandboxSettingsService>();
            services.AddSingleton<IConfigurationHistoryService, ConfigurationHistoryService>();
            services.AddSingleton<IMetricsStore, JsonMetricsStore>();
            services.AddSingleton<ISystemUpdateRunner, SystemdUpdateRunner>();
            services.AddSingleton<ServerRestartCoordinator>();
            services.AddSingleton<IUpdateManager, UpdateManager>();
            services.AddSingleton<ICardinalStore, JsonCardinalStore>();
            services.AddSingleton<ICardinalActionExecutor, CardinalActionExecutor>();
            services.AddSingleton<ICardinalManager, CardinalManager>();
            services.AddSingleton<ICardinalLlmClient, AnthropicCardinalClient>();
            services.AddSingleton<CardinalLogCollector>();
            services.AddSingleton<CardinalBridgeResultCollector>();
            services.AddScoped<ModManagerService>();
        }

        services.AddPooledDbContextFactory<ManagerDbContext>((provider, options) =>
        {
            var paths = provider.GetRequiredService<IManagerPaths>();
            options.UseSqlite($"Data Source={paths.DatabasePath}");
        });

        services.AddScoped<IServerInstanceRepository, ServerInstanceRepository>();
        services.AddSingleton<ISteamCmdClient, SteamCmdClient>();
        services.AddScoped<IInfrastructureInitializer, InfrastructureInitializer>();
        return services;
    }
}
