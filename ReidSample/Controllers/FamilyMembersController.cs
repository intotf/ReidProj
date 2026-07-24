using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IO;
using ReIdSample.Data;
using ReIdSample.Models;
using ReIdSample.Models.Dtos;
using ReIdSample.Services;

namespace ReIdSample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FamilyMembersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ReidFeatureClient _reidClient;
    private readonly RecyclableMemoryStreamManager _streamManager;
    private readonly ILogger<FamilyMembersController> _logger;

    public FamilyMembersController(AppDbContext db, ReidFeatureClient reidClient, RecyclableMemoryStreamManager streamManager, ILogger<FamilyMembersController> logger)
    {
        _db = db;
        _reidClient = reidClient;
        _streamManager = streamManager;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有家庭成员
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<FamilyMemberResponse>>> GetAll()
    {
        var members = await _db.FamilyMembers
            .Include(m => m.Photos)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new FamilyMemberResponse
            {
                Id = m.Id,
                Name = m.Name,
                CreatedAt = m.CreatedAt,
                PhotoCount = m.Photos.Count,
                Photos = m.Photos.Select(p => new PhotoResponse
                {
                    Id = p.Id,
                    CreatedAt = p.CreatedAt
                }).ToList()
            })
            .ToListAsync();

        return Ok(members);
    }

    /// <summary>
    /// 获取单个家庭成员详情（含照片列表）
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FamilyMemberResponse>> GetById(Guid id)
    {
        var member = await _db.FamilyMembers
            .Include(m => m.Photos)
            .Where(m => m.Id == id)
            .Select(m => new FamilyMemberResponse
            {
                Id = m.Id,
                Name = m.Name,
                CreatedAt = m.CreatedAt,
                PhotoCount = m.Photos.Count,
                Photos = m.Photos.Select(p => new PhotoResponse
                {
                    Id = p.Id,
                    CreatedAt = p.CreatedAt
                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (member is null)
            return NotFound(new { error = "家庭成员不存在" });

        return Ok(member);
    }

    /// <summary>
    /// 创建家庭成员并上传注册照片
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<FamilyMemberResponse>> Create([FromForm] CreateFamilyMemberRequest request)
    {
        if (request.Photo is null || request.Photo.Length == 0)
            return BadRequest(new { error = "请上传注册照片" });

        // 1. 读取照片 bytes → 调用 ReidFeature 提取特征
        List<ReidPersonDetection> detections;
        using (var ms = _streamManager.GetStream())
        {
            await request.Photo.CopyToAsync(ms);
            ms.Position = 0;
            detections = await _reidClient.DetectAsync(ms);
        }


        if (detections.Count == 0)
            return BadRequest(new { error = "照片中未检测到人物" });

        // 2. 创建家庭成员
        var member = new FamilyMember
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            CreatedAt = DateTime.UtcNow
        };

        // 3. 为每个检测到的人物创建照片记录（注册多个特征）
        foreach (var det in detections)
        {
            member.Photos.Add(new FamilyMemberPhoto
            {
                Id = Guid.NewGuid(),
                FamilyMemberId = member.Id,
                FeatureVector = det.Features,
                CreatedAt = DateTime.UtcNow
            });
        }

        _db.FamilyMembers.Add(member);
        await _db.SaveChangesAsync();

        _logger.LogInformation("创建家庭成员 '{Name}' (Id={Id})，注册 {Count} 个特征",
            member.Name, member.Id, detections.Count);

        return CreatedAtAction(nameof(GetById), new { id = member.Id }, new FamilyMemberResponse
        {
            Id = member.Id,
            Name = member.Name,
            CreatedAt = member.CreatedAt,
            PhotoCount = member.Photos.Count,
            Photos = member.Photos.Select(p => new PhotoResponse
            {
                Id = p.Id,
                CreatedAt = p.CreatedAt
            }).ToList()
        });
    }

    /// <summary>
    /// 删除家庭成员（级联删除照片）
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var member = await _db.FamilyMembers.FindAsync(id);
        if (member is null)
            return NotFound(new { error = "家庭成员不存在" });

        _db.FamilyMembers.Remove(member);
        await _db.SaveChangesAsync();

        _logger.LogInformation("已删除家庭成员 '{Name}' (Id={Id})", member.Name, id);
        return NoContent();
    }

    /// <summary>
    /// 为已有成员追加注册照片
    /// </summary>
    [HttpPost("{id:guid}/photos")]
    public async Task<IActionResult> AddPhoto(Guid id, IFormFile photo)
    {
        if (photo is null || photo.Length == 0)
            return BadRequest(new { error = "请上传照片" });

        var member = await _db.FamilyMembers.FindAsync(id);
        if (member is null)
            return NotFound(new { error = "家庭成员不存在" });

        // 1. 读取照片 bytes → 调用 ReidFeature 提取特征
        List<ReidPersonDetection> detections;
        using (var ms = _streamManager.GetStream())
        {
            await photo.CopyToAsync(ms);
            ms.Position = 0;
            detections = await _reidClient.DetectAsync(ms);
        }


        if (detections.Count == 0)
        {
            return BadRequest(new { error = "照片中未检测到人物" });
        }

        // 2. 为每个检测到的人物创建照片记录
        var createdPhotos = new List<PhotoResponse>();
        foreach (var det in detections)
        {
            var photoEntity = new FamilyMemberPhoto
            {
                Id = Guid.NewGuid(),
                FamilyMemberId = member.Id,
                FeatureVector = det.Features,
                CreatedAt = DateTime.UtcNow
            };
            _db.FamilyMemberPhotos.Add(photoEntity);
            createdPhotos.Add(new PhotoResponse
            {
                Id = photoEntity.Id,
                CreatedAt = photoEntity.CreatedAt
            });
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("为成员 '{Name}' (Id={Id}) 追加 {Count} 个注册特征",
            member.Name, id, detections.Count);

        return Ok(createdPhotos);
    }
}
