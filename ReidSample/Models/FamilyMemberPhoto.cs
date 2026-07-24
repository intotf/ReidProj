namespace ReIdSample.Models;

public class FamilyMemberPhoto
{
    public Guid Id { get; set; }

    public Guid FamilyMemberId { get; set; }

    public byte[] FeatureVector { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public FamilyMember FamilyMember { get; set; } = null!;
}
