namespace Avocado.Server.Features.Users.Endpoints.Dtos;

public sealed record UserInput(string DisplayName, string? Email, long HourlyRateCents, bool IsActive = true)
{
    public string? Validate() => this switch
    {
        { DisplayName: var name } when string.IsNullOrWhiteSpace(name) =>
            "Le nom est obligatoire.",
        { HourlyRateCents: < 0 } =>
            "Le taux horaire ne peut pas être négatif.",
        _ => null,
    };
}
