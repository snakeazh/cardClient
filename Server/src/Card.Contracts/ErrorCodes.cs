namespace CardShare.Contracts;

public static class ErrorCodes
{
    public const string Unauthorized = "unauthorized";
    public const string GuestDisabled = "guest_disabled";
    public const string ProviderNotConfigured = "provider_not_configured";
    public const string InvalidRequest = "invalid_request";
    public const string InsufficientEnergy = "insufficient_energy";
    public const string InsufficientGold = "insufficient_gold";
    public const string LevelLocked = "level_locked";
    public const string HeroLocked = "hero_locked";
    public const string RunNotFound = "run_not_found";
    public const string RunAlreadySettled = "run_already_settled";
    public const string RunMismatch = "run_mismatch";
    public const string ActiveRunExists = "active_run_exists";
    public const string TalentPoolEmpty = "talent_pool_empty";
    public const string AdLimitReached = "ad_limit_reached";
    public const string Conflict = "conflict";
}
