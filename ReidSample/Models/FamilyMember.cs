using System.ComponentModel.DataAnnotations;

namespace ReIdSample.Models;

public class FamilyMember
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public List<FamilyMemberPhoto> Photos { get; set; } = [];
}
