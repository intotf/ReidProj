namespace ReIdSample.Models.Dtos;

public class FamilyMemberResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int PhotoCount { get; set; }
    public List<PhotoResponse> Photos { get; set; } = [];
}

public class PhotoResponse
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
}
