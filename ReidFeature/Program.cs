using Microsoft.IO;
using ReidFeature.Handlers;
using ReidFeature.Services;

namespace ReidFeature
{
    /// <summary>
    /// 应用程序入口点
    /// </summary>
    public class Program
    {
        /// <summary>
        /// 应用程序入口 — 构建服务、注册中间件、启动 Kestrel 主机
        /// </summary>
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            // ── ONNX Runtime 配置 ─────────────────────────
            builder.Services.Configure<OnnxSessionOptions>(
                builder.Configuration.GetSection("Onnx"));

            // ── JSON 序列化 ──────────────────────────────
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            // ── Kestrel 配置 ──────────────────────────────
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Limits.MaxRequestBodySize = 20 * 1024 * 1024;
            });

            // ── 注册服务 ──────────────────────────────────
            builder.Services.AddSingleton(new RecyclableMemoryStreamManager());
            builder.Services.AddSingleton<YoloDetector>();
            builder.Services.AddSingleton<ReIdExtractor>();
            builder.Services.AddSingleton<FaceDetector>();

            builder.Services.AddOpenApi();

            var app = builder.Build();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "v1");
            });

            // ── 路由端点 ──────────────────────────────────
            app.MapOpenApi();

            app.Map("/", context => context.Response.WriteAsync("HealthCheck"))
               .WithName("HealthCheck");

            app.MapPost("/detect", DetectHandler.HandleAsync)
               .WithName("DetectPersons")
               .Accepts<byte[]>("image/png", "image/jpeg", "image/bmp");

            app.Run();
        }
    }
}
