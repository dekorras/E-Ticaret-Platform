using Dekorras.Application.Localization.Commands;
using MediatR;

namespace Dekorras.Api.Jobs;

/// <summary>Hangfire'ın çağırabileceği somut bir sınıf/metot gerekiyor (generic `ISender.Send`'i
/// doğrudan zamanlayamaz) - bu yalnızca `RefreshExchangeRatesCommand`'a ince bir sarmalayıcı.</summary>
public sealed class ExchangeRateRefreshJob(ISender sender)
{
    public Task RunAsync() => sender.Send(new RefreshExchangeRatesCommand(), CancellationToken.None);
}
