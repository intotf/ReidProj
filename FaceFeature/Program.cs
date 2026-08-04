using FaceFeature.Handlers;
using FaceFeature.Services;

namespace FaceFeature
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

            // ── 清晰度筛选配置 ────────────────────────────
            builder.Services.Configure<FaceQualityOptions>(
                builder.Configuration.GetSection("FaceQuality"));

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
            builder.Services.AddSingleton<FaceDetector>();
            builder.Services.AddSingleton<FaceExtractor>();
            builder.Services.AddSingleton<DetectService>();
            builder.Services.AddSingleton<FaceGroupService>();

            builder.Services.AddHttpClient();

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

            app.MapPost("/detect/image", DetectHandler.HandleImageAsync)
               .WithName("DetectImage")
               .Accepts<byte[]>("application/octet-stream");

            app.MapPost("/detect/stream", DetectHandler.HandleStreamAsync)
               .WithName("DetectStream")
               .Accepts<byte[]>("application/octet-stream");

            app.MapPost("/recognize/stream/{groupId}", RecognizeHandler.HandleStreamAsync)
               .WithName("RecognizeStream")
               .Accepts<byte[]>("application/octet-stream");

            // ── 人脸管理 ──────────────────────────────────
            app.MapPost("/faces/{groupId}/register", FaceGroupHandler.RegisterAsync)
               .WithName("RegisterFace")
               .Accepts<byte[]>("application/octet-stream");

            app.MapGet("/faces/{groupId}", FaceGroupHandler.ListAsync)
               .WithName("ListFaces");

            app.MapGet("/faces/{groupId}/{faceId}", FaceGroupHandler.GetAsync)
               .WithName("GetFace");

            app.MapDelete("/faces/{groupId}/{faceId}", FaceGroupHandler.DeleteAsync)
               .WithName("DeleteFace");

            app.Run();
        }
    }
}
