namespace MediaDownloader;

/// <summary>
/// 应用程序配置
/// </summary>
public sealed class AppConfig
{
    /// <summary>API 基础地址</summary>
    public string ApiBaseUrl { get; set; } = "https://iot.anyfree.com";

    /// <summary>授权 Token</summary>
    public string AuthToken { get; set; } = "";

    /// <summary>默认设备 ID 列表（多个时按设备文件夹存放）</summary>
    public List<string> DefaultDeviceIds { get; set; } = [];

    /// <summary>下载保存根目录</summary>
    public string DownloadDir { get; set; } = "./downloads";

    /// <summary>每页条数</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>最大下载文件数（0 表示全部）</summary>
    public int MaxFiles { get; set; }

    /// <summary>最小创建时间过滤（ISO 8601 格式，如 2026-07-27T00:00:00Z），为空时不过滤</summary>
    public string? MinCreationTime { get; set; }

    /// <summary>AWS S3 配置</summary>
    public AwsConfig Aws { get; set; } = new();
}

public sealed class AwsConfig
{
    /// <summary>S3 访问密钥</summary>
    public string AccessKey { get; set; } = "";

    /// <summary>S3 秘密密钥</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>S3 区域</summary>
    public string Region { get; set; } = "ap-northeast-1";

    /// <summary>S3 存储桶名称</summary>
    public string BucketName { get; set; } = "";
}
