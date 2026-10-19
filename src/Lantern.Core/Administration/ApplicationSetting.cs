using System.ComponentModel.DataAnnotations;

namespace Lantern.Core.Administration;

public sealed class ApplicationSetting
{
    public int Id { get; set; } = 1;
    [Required, StringLength(80, MinimumLength = 1)]
    public string DisplayName { get; set; } = "Lantern";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 80 || DisplayName.Any(char.IsControl))
            throw new ValidationException("Application name must contain 1–80 characters without control characters.");
        DisplayName = DisplayName.Trim();
    }
}

public interface IApplicationSettingsService
{
    Task<ApplicationSetting> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationSetting settings, CancellationToken cancellationToken = default);
}
