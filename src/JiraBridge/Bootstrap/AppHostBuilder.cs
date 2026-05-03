using JiraBridge.Application.Configuration;
using JiraBridge.Application.Sync;
using JiraBridge.Application.Validation;
using JiraBridge.Host;
using JiraBridge.Host.Terminal;
using JiraBridge.Application.Abstractions;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.Infrastructure.Repository;
using JiraBridge.Infrastructure.Storage;
using JiraBridge.Navigation.Commands;
using JiraBridge.Navigation.Menu;
using JiraBridge.Screens.Configuration;
using JiraBridge.Screens.Home;
using JiraBridge.Screens.Sync;
using JiraBridge.Screens.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JiraBridge.Bootstrap;

public static class AppHostBuilder
{
  public static IHost Build(string[] args)
  {
    var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

    builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

    builder.Services.AddSingleton<AppRunner>();
    builder.Services.AddSingleton<TerminalLoop>();
    builder.Services.AddSingleton<InteractiveShellController>();
    builder.Services.AddSingleton<KeyboardDispatcher>();
    builder.Services.AddSingleton<ScreenRenderer>();
    builder.Services.AddSingleton<OperationProgressTracker>();
    builder.Services.AddSingleton<ViewportState>();
    builder.Services.AddSingleton<MenuNavigator>();
    builder.Services.AddSingleton<CommandPalette>();
    builder.Services.AddSingleton<CommandSuggestionEngine>();

    builder.Services.AddSingleton<HomeScreen>();
    builder.Services.AddSingleton<HomeViewModel>();
    builder.Services.AddSingleton<ConfigurationScreen>();
    builder.Services.AddSingleton<ConfigurationViewModel>();
    builder.Services.AddSingleton<ValidationScreen>();
    builder.Services.AddSingleton<ValidationViewModel>();
    builder.Services.AddSingleton<PushScreen>();
    builder.Services.AddSingleton<PullScreen>();
    builder.Services.AddSingleton<ConflictsScreen>();
    builder.Services.AddSingleton<ResolveConflictScreen>();

    builder.Services.AddSingleton<ConfigureRepositoryCommandHandler>();
    builder.Services.AddSingleton<ValidateRepositoryCommandHandler>();
    builder.Services.AddSingleton<PushChangesCommandHandler>();
    builder.Services.AddSingleton<PullChangesCommandHandler>();
    builder.Services.AddSingleton<GetConflictsQueryHandler>();
    builder.Services.AddSingleton<ResolveConflictCommandHandler>();

    builder.Services.AddSingleton<IJiraApiClientFactory, JiraApiClientFactory>();
    builder.Services.AddSingleton<IOperationProgressSink>(provider => provider.GetRequiredService<OperationProgressTracker>());
    builder.Services.AddSingleton<IRepositoryMetadataRefresher, RepositoryMetadataRefresher>();
    builder.Services.AddSingleton<IRepositoryBootstrapper, RepositoryBootstrapper>();
    builder.Services.AddSingleton<IBacklogValidator, BacklogValidator>();
    builder.Services.AddSingleton<ISyncExecutor, SyncExecutor>();
    builder.Services.AddSingleton<IConflictStore, ConflictStore>();
    builder.Services.AddSingleton<IConflictResolver, ConflictResolver>();

    return builder.Build();
  }
}
