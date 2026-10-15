using System.ComponentModel.DataAnnotations;

namespace FrameRouter;

internal sealed class FrameRouterOptions
{
    public const string SectionName = "FrameRouter";

    [Required]
    public string DataDirectory { get; set; } = "data";

    [Required]
    public string TimeZone { get; set; } = "Europe/Warsaw";

    // Empty disables POST /api/config.
    public string ConfigToken { get; set; } = string.Empty;

    // Empty disables /api/control (the control page and screen overrides).
    public string ControlToken { get; set; } = string.Empty;
}
