using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace SewerPilot.Mcp.Server
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length != 0) { Console.Error.WriteLine("MCP_STARTUP_ARGUMENTS_NOT_ALLOWED"); return 2; }
            try
            {
                var query = new ReadOnlyProjectQuery(Environment.GetEnvironmentVariable("SEWERPILOT_PROJECT_DIR"));
                var projectTools = new DesignBasisTools(query);
                var calculationTools = new StormFlowCalculationTools();
                var gravityTools = new GravityCandidatePreviewTools(query);
                var civil3dTools = new Civil3dLiveTools();
                // No appsettings, user secrets, network providers or assembly-wide discovery.
                var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
                builder.Logging.ClearProviders();
                builder.Logging.AddProvider(new ClassificationOnlyLogger());
                builder.Services.AddMcpServer(options =>
                    options.ServerInfo = new Implementation { Name = "SewerPilot.Mcp.Server", Version = "1.0.0" })
                    .WithStdioServerTransport()
                    .WithListToolsHandler((context, token) =>
                    {
                        var result = projectTools.List();
                        calculationTools.RegisterTools(result.Tools);
                        gravityTools.RegisterTools(result.Tools);
                        if (Environment.GetEnvironmentVariable("SEWERPILOT_ENABLE_CIVIL3D_TOOLS") == "1")
                        {
                            civil3dTools.RegisterTools(result.Tools);
                        }
                        return new ValueTask<ListToolsResult>(result);
                    })
                    .WithCallToolHandler((context, token) =>
                    {
                        var request = context.Params;
                        if (request != null && calculationTools.CanHandle(request.Name))
                        {
                            return new ValueTask<CallToolResult>(calculationTools.Handle(request));
                        }
                        if (request != null && gravityTools.CanHandle(request.Name))
                        {
                            return new ValueTask<CallToolResult>(gravityTools.Handle(request));
                        }
                        if (request != null && civil3dTools.CanHandle(request.Name))
                        {
                            return new ValueTask<CallToolResult>(civil3dTools.Handle(request));
                        }
                        return new ValueTask<CallToolResult>(projectTools.Call(request));
                    });
                using (var host = builder.Build()) await host.RunAsync();
                return 0;
            }
            catch (Exception)
            {
                Console.Error.WriteLine("MCP_STARTUP_OR_TRANSPORT_FAILURE");
                return 1;
            }
        }

        // SDK diagnostics can contain request text/exceptions. Only severity is emitted.
        private sealed class ClassificationOnlyLogger : ILoggerProvider, ILogger
        {
            public ILogger CreateLogger(string categoryName) => this;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
            public IDisposable BeginScope<TState>(TState state) => null;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception error, Func<TState, Exception, string> formatter)
            {
                if (IsEnabled(level)) Console.Error.WriteLine("MCP_DIAGNOSTIC: " + level);
            }
            public void Dispose() { }
        }
    }
}
