using Microsoft.EntityFrameworkCore;
using ReIdSample.Data;
using ReIdSample.Services;

namespace ReIdSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ── 数据库 ──────────────────────────────────
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

            // ── 远程调用 ──────────────────────────────────
            builder.Services.AddHttpClient<ReidFeatureClient>(client =>
            {
                client.BaseAddress = new Uri(builder.Configuration.GetValue<string>("ReidFeature:BaseUrl")
                    ?? "http://localhost:5000");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // ── 业务服务 ──────────────────────────────────
            builder.Services.AddScoped<MatchingService>();

            // ── 控制器 + Swagger ──────────────────────────
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new()
                {
                    Title = "ReIdSample API",
                    Version = "v1",
                    Description = "家庭成员管理与照片匹配系统"
                });
            });

            var app = builder.Build();

            // ── 自动建表（开发环境） ──────────────────────
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
            }

            // ── 中间件管道 ──────────────────────────────
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "ReIdSample v1");
                options.RoutePrefix = "swagger";
            });

            app.MapControllers();

            app.Run();
        }
    }
}
