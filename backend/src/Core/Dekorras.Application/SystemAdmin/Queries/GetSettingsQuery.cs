using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.SystemAdmin;
using MediatR;

namespace Dekorras.Application.SystemAdmin.Queries;

public sealed record SettingDto(string Key, string Value);

/// <summary>Belirli anahtarların (bkz. `ContactSettingKeys`) değerlerini döner - hiç ayarlanmamış bir
/// anahtar sonuçta HİÇ görünmez (boş/varsayılan değil, tamamen YOK), çağıran taraf
/// `.FirstOrDefault(...)?.Value` ile "ayarlanmamış" durumunu doğal olarak ele alır.</summary>
public sealed record GetSettingsQuery(IReadOnlyCollection<string> Keys) : IRequest<IReadOnlyCollection<SettingDto>>;

public sealed class GetSettingsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetSettingsQuery, IReadOnlyCollection<SettingDto>>
{
    public Task<IReadOnlyCollection<SettingDto>> Handle(GetSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = unitOfWork.Repository<Setting>().Query()
            .Where(s => request.Keys.Contains(s.Key))
            .Select(s => new SettingDto(s.Key, s.Value))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<SettingDto>>(settings);
    }
}
