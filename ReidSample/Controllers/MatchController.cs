using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;
using ReIdSample.Models.Dtos;
using ReIdSample.Services;

namespace ReIdSample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchController : ControllerBase
{
    private readonly MatchingService _matchingService;
    private readonly IConfiguration _configuration;
    private readonly RecyclableMemoryStreamManager _streamManager;
    private readonly ILogger<MatchController> _logger;

    public MatchController(MatchingService matchingService, IConfiguration configuration, RecyclableMemoryStreamManager streamManager, ILogger<MatchController> logger)
    {
        _matchingService = matchingService;
        _configuration = configuration;
        _streamManager = streamManager;
        _logger = logger;
    }

    /// <summary>
    /// 上传照片，匹配最相似的家庭成员
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MatchResponse>> Match(IFormFile photo)
    {
        if (photo is null || photo.Length == 0)
        {
            return BadRequest(new { error = "请上传照片" });
        }

        var threshold = _configuration.GetValue<float>("Matching:SimilarityThreshold", 0.6f);
        using var ms = _streamManager.GetStream();
        await photo.CopyToAsync(ms);
        ms.Position = 0;

        _logger.LogInformation("匹配请求: 图片大小={Len} bytes, 阈值={Threshold}", ms.Length, threshold);
        var result = await _matchingService.MatchAsync(ms, threshold);
        return result.Detections.Count == 0 
            ? Ok(new MatchResponse { Detections = [], Threshold = threshold }) 
            : Ok(result);
    }
}
