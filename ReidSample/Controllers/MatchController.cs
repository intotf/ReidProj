using Microsoft.AspNetCore.Mvc;
using ReIdSample.Models.Dtos;
using ReIdSample.Services;

namespace ReIdSample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchController : ControllerBase
{
    private readonly MatchingService _matchingService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MatchController> _logger;

    public MatchController(MatchingService matchingService, IConfiguration configuration, ILogger<MatchController> logger)
    {
        _matchingService = matchingService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 上传照片，匹配最相似的家庭成员
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MatchResponse>> Match(IFormFile photo)
    {
        if (photo is null || photo.Length == 0)
            return BadRequest(new { error = "请上传照片" });

        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            await photo.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }

        var threshold = _configuration.GetValue<float>("Matching:SimilarityThreshold", 0.6f);
        _logger.LogInformation("匹配请求: 图片大小={Len} bytes, 阈值={Threshold}", imageBytes.Length, threshold);

        var result = await _matchingService.MatchAsync(imageBytes, threshold);

        if (result.Detections.Count == 0)
            return Ok(new MatchResponse
            {
                Detections = [],
                Threshold = threshold,
            });

        return Ok(result);
    }
}
