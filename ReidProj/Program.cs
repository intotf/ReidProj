using Microsoft.IO;
using ReidProj.Handlers;
using ReidProj.Services;

namespace ReidProj
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            // ── JSON 序列化 ──────────────────────────────
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            // ── Kestrel 配置 ──────────────────────────────
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.ListenAnyIP(9000);
                k.Limits.MaxRequestBodySize = 20 * 1024 * 1024;
            });

            // ── 注册服务 ──────────────────────────────────
            builder.Services.AddSingleton(new RecyclableMemoryStreamManager());
            builder.Services.AddSingleton<ImageUtils>();
            builder.Services.AddSingleton<YoloDetector>();
            builder.Services.AddSingleton<ReIdExtractor>();

            var app = builder.Build();

            // ── 路由端点 ──────────────────────────────────
            app.MapGet("/", () => Results.Ok(new { status = "healthy", service = "reid-api" }))
               .WithName("HealthCheck");

            app.MapPost("/detect", DetectHandler.HandleAsync)
               .WithName("DetectPersons");

            app.Run();
        }
    }
}
