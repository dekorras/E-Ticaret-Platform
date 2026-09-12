using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.SystemAdmin;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.SystemAdmin.Commands;

public sealed record UpsertSettingCommand(string Key, string Value) : IRequest<Unit>;

public sealed class UpsertSettingCommandValidator : AbstractValidator<UpsertSettingCommand>
{
    public UpsertSettingCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Value).MaximumLength(1000);
    }
}

public sealed class UpsertSettingCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpsertSettingCommand, Unit>
{
    public async Task<Unit> Handle(UpsertSettingCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Setting>();
        var existing = repository.Query().FirstOrDefault(s => s.Key == request.Key);

        if (existing is not null)
        {
            existing.SetValue(request.Value);
        }
        else
        {
            await repository.AddAsync(new Setting(request.Key, request.Value), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
