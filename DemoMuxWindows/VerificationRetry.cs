using System.IO;
namespace DemoMux;

internal enum PublicFailure { Dns, Timeout, Connection, Handshake, Certificate }
internal sealed class PublicVerificationException(PublicFailure failure, string message, Exception? inner = null) : IOException(message, inner)
{
    public PublicFailure Failure { get; } = failure;
    public bool Retryable => Failure != PublicFailure.Certificate;
}
internal sealed record VerificationProgress(int Attempt, int? RetryInSeconds, string? LastError);
internal static class VerificationRetry
{
    internal static async Task RunAsync(Func<CancellationToken, Task> check, Action<VerificationProgress> report, CancellationToken token,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        for (int attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            report(new(attempt, null, null));
            try { await check(token); token.ThrowIfCancellationRequested(); return; }
            catch (PublicVerificationException error) when (error.Retryable && !token.IsCancellationRequested)
            {
                var seconds = attempt switch { 1 => 2, 2 => 4, 3 => 8, _ => 15 };
                for (; seconds > 0; seconds--)
                {
                    token.ThrowIfCancellationRequested(); report(new(attempt, seconds, error.Message));
                    await delay(TimeSpan.FromSeconds(1), token);
                }
            }
        }
    }
}
