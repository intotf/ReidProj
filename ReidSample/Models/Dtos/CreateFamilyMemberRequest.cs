using System.ComponentModel.DataAnnotations;

namespace ReIdSample.Models.Dtos;

public class CreateFamilyMemberRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public IFormFile Photo { get; set; } = null!;
}
