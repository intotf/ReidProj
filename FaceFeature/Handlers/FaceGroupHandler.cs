using FaceFeature.Payloads;
using FaceFeature.Services;

namespace FaceFeature.Handlers;

/// <summary>
/// 人脸管理处理器 — 指定分组下的人脸注册 / 查询 / 删除
/// </summary>
public static class FaceGroupHandler
{
    /// <summary>
    /// 注册人脸：POST /faces/{groupId}/register?name=xxx，请求体为原始图片字节
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="faceGroupService">人脸分组管理服务</param>
    /// <param name="groupId">分组 ID</param>
    /// <param name="name">人物名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注册结果（成功时含人脸信息）</returns>
    public static async Task<IResult> RegisterAsync(
        HttpContext context,
        FaceGroupService faceGroupService,
        string groupId,
        string? name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest(new FaceError("缺少 name 参数"));
        }

        FaceRegistrationResult result;
        try
        {
            result = await faceGroupService.RegisterAsync(groupId, name, context.Request.Body, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new FaceError(ex.Message));
        }

        return result.Success
            ? Results.Ok(result.Face)
            : Results.BadRequest(new FaceError(result.Error!));
    }

    /// <summary>
    /// 查询分组下所有人脸：GET /faces/{groupId}
    /// </summary>
    /// <param name="faceGroupService">人脸分组管理服务</param>
    /// <param name="groupId">分组 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分组下所有人脸信息（不含特征向量）</returns>
    public static async Task<IResult> ListAsync(
        FaceGroupService faceGroupService,
        string groupId,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await faceGroupService.ListAsync(groupId, cancellationToken);
            return Results.Ok(items);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new FaceError(ex.Message));
        }
    }

    /// <summary>
    /// 查询单张人脸（含特征向量）：GET /faces/{groupId}/{faceId}
    /// </summary>
    /// <param name="faceGroupService">人脸分组管理服务</param>
    /// <param name="groupId">分组 ID</param>
    /// <param name="faceId">人脸 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>人脸信息（含特征向量），不存在时返回 404</returns>
    public static async Task<IResult> GetAsync(
        FaceGroupService faceGroupService,
        string groupId,
        string faceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var face = await faceGroupService.GetAsync(groupId, faceId, includeFeatures: true, cancellationToken);
            return face is null ? Results.NotFound() : Results.Ok(face);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new FaceError(ex.Message));
        }
    }

    /// <summary>
    /// 删除人脸：DELETE /faces/{groupId}/{faceId}
    /// </summary>
    /// <param name="faceGroupService">人脸分组管理服务</param>
    /// <param name="groupId">分组 ID</param>
    /// <param name="faceId">人脸 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除成功时返回响应，人脸不存在时返回 404</returns>
    public static async Task<IResult> DeleteAsync(
        FaceGroupService faceGroupService,
        string groupId,
        string faceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await faceGroupService.DeleteAsync(groupId, faceId, cancellationToken);
            return deleted ? Results.Ok(new FaceDeleteResponse(true)) : Results.NotFound();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new FaceError(ex.Message));
        }
    }
}
