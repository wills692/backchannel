using BackChannel.App.Commands;
using BackChannel.App.Configuration;
using BackChannel.App.Logging;
using BackChannel.App.Runtime;
using BackChannel.App.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

builder.Logging.ClearProviders();
builder.Logging.AddProvider(
    new DailyFileLoggerProvider(
        Path.Combine(AppContext.BaseDirectory, "logs")));

builder.Services
    .AddOptions<BackChannelOptions>()
    .Bind(builder.Configuration.GetSection(BackChannelOptions.SectionName))
    .Validate(
        BackChannelOptions.IsValid,
        "BackChannel configuration contains an invalid setting.")
    .ValidateOnStart();

builder.Services.AddSingleton<BackChannelNode>();
builder.Services.AddSingleton<IHostedService>(
    static services => services.GetRequiredService<BackChannelNode>());
builder.Services.AddSingleton<IBackChannelTerminal, SpectreBackChannelTerminal>();
builder.Services.AddSingleton<ChatSession>();

builder.Services.AddSingleton<IBackChannelCommand, HelloCommand>();
builder.Services.AddSingleton<IBackChannelCommand, NickCommand>();
builder.Services.AddSingleton<IBackChannelCommand, PeersCommand>();
builder.Services.AddSingleton<IBackChannelCommand, MessageCommand>();
builder.Services.AddSingleton<IBackChannelCommand, GroupCommand>();
builder.Services.AddSingleton<IBackChannelCommand, HelpCommand>();
builder.Services.AddSingleton<IBackChannelCommand, QuitCommand>();
builder.Services.AddSingleton<CommandDispatcher>();

builder.Services.AddHostedService<TerminalShell>();

await builder.Build().RunAsync();
