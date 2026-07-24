namespace ReIdSample.Models.Dtos;

public class MatchResponse
{
    public List<DetectionResult> Detections { get; set; } = [];
    public float Threshold { get; set; }
}

public class DetectionResult
{
    public BoundingRect Bbox { get; set; } = null!;
    public float Confidence { get; set; }
    public List<PersonMatch> Matches { get; set; } = [];
}

public class BoundingRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class PersonMatch
{
    public Guid FamilyMemberId { get; set; }
    public string FamilyMemberName { get; set; } = string.Empty;
    public Guid PhotoId { get; set; }
    public float Similarity { get; set; }
    public bool Matched { get; set; }
}
