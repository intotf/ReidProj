using ReidFeature.Handlers;
using ReidFeature.Payloads;
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
            builder.Services.AddSingleton<YoloDetector>();
            builder.Services.AddSingleton<ReIdExtractor>();
            builder.Services.AddSingleton<PoseEstimator>();
            builder.Services.AddSingleton<ByteTrackTracker>();
            builder.Services.AddSingleton<TrackFusionService>();
            builder.Services.AddSingleton<FamilyGalleryService>();
            builder.Services.AddSingleton<IFamilyMemberProvider>(sp =>
                sp.GetRequiredService<FamilyGalleryService>());
            builder.Services.AddSingleton<DetectService>();

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

            // ── 检测端点（仅视频流） ──────────────────────
            app.MapPost("/detect/h264stream", DetectHandler.HandleH264StreamAsync)
               .WithName("DetectH264Stream")
               .Accepts<byte[]>("application/octet-stream");

            app.MapPost("/detect/h265stream", DetectHandler.HandleH265StreamAsync)
               .WithName("DetectH265Stream")
               .Accepts<byte[]>("application/octet-stream");

            // ── 识别端点（仅视频流，返回单个最佳匹配） ────
            app.MapPost("/recognize/h264stream/{groupId}", RecognizeHandler.HandleH264StreamAsync)
               .WithName("RecognizeH264Stream")
               .Accepts<byte[]>("application/octet-stream");

            app.MapPost("/recognize/h265stream/{groupId}", RecognizeHandler.HandleH265StreamAsync)
               .WithName("RecognizeH265Stream")
               .Accepts<byte[]>("application/octet-stream");

            // ── 家庭成员管理端点 ─────────────────────────
            app.MapPost("/family/enroll/{groupId}/{memberName}", EnrollmentHandler.HandleEnrollAsync)
               .WithName("FamilyEnroll")
               .Accepts<byte[]>("application/octet-stream");

            app.MapDelete("/family/{groupId}/{memberId}", async (
                string groupId,
                string memberId,
                IFamilyMemberProvider provider,
                CancellationToken ct) =>
            {
                var ok = await provider.DeleteAsync(groupId, memberId, ct);
                return ok ? Results.Ok() : Results.NotFound();
            }).WithName("FamilyDelete");

            app.MapGet("/family/{groupId}", async (
                string groupId,
                IFamilyMemberProvider provider,
                CancellationToken ct) =>
            {
                var members = await provider.ListAsync(groupId, ct);
                return Results.Ok(members);
            }).WithName("FamilyList");

            app.Run();
        }
    }
}
