namespace UseScope.Desktop;

internal static class SourceFactory
{
    internal static IAccountSource Create(Identity identity) => MeterStorage.Enabled(identity) || Providers.Get(identity.Vendor).Connection == ConnectionKind.LocalMeter
        ? new LocalMeterSource(MeterRuntime.Shared.Ledger) : identity.Vendor switch {
        "codex" => new CodexSource(), "claude" => new ClaudeSource(), "nous" => new NousSource(),
        "anthropic-api" => new AnthropicAnalyticsSource(), "meta" => new MuseSource(), "gemini" => new GoogleProjectSource(),
        "minimax" or "alibaba-coding" or "alibaba-token" => new PlanSource(), "minimax-api" => new MiniMaxBalanceSource(),
        "qwen" or "wan" or "alibaba-cloud" => new AlibabaBillingSource(),
        "arcee" => new ArceeSource(), "stepfun" => new StepFunSource(), "mistral-admin" => new MistralAdminSource(),
        "digitalocean-billing" => new DigitalOceanBillingSource(), _ => new ApiAccountSource()
    };
}
