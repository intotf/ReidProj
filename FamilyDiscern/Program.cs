using System;
using System.Threading;
using Avalonia;
using FamilyDiscern.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FamilyDiscern;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // MCP 客户端使用专用模式，避免启动 GUI 或由其他输出污染 stdio。
        if (Array.Exists(args, arg => string.Equals(arg, "--mcp", StringComparison.OrdinalIgnoreCase)))
        {
            StartMcpServer();
            return;
        }

        // 正常桌面模式仍在同一进程后台启动 MCP Server。
        var mcpThread = new Thread(StartMcpServer) { IsBackground = true };
        mcpThread.Start();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void StartMcpServer()
    {
        try
        {
            var builder = Host.CreateApplicationBuilder();

            // MCP stdio 要求 stdout 仅用于 JSON-RPC；所有日志统一写入 stderr。
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(options =>
                options.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            var host = builder.Build();
            host.RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MCP Server 启动失败: {ex.Message}");
        }
    }
}
