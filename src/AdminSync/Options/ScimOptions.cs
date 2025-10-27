using System.ComponentModel.DataAnnotations;

namespace AdminSync.Options;

public class ScimOptions
{
    [Required]
    [MinLength(12)]
    public string SharedSecret { get; set; } = string.Empty;
}
