using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record CheckDto(Guid Id, string CheckNumber, string LedgerAccountName, decimal AmountTry, DateTime DueDateUtc, PaperInstrumentStatus Status);
public sealed record PromissoryNoteDto(Guid Id, string NoteNumber, string LedgerAccountName, decimal AmountTry, DateTime DueDateUtc, PaperInstrumentStatus Status);

public sealed record GetChecksQuery(PaperInstrumentStatus? StatusFilter = null) : IRequest<IReadOnlyCollection<CheckDto>>;

public sealed class GetChecksQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetChecksQuery, IReadOnlyCollection<CheckDto>>
{
    public Task<IReadOnlyCollection<CheckDto>> Handle(GetChecksQuery request, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Repository<Check>().Query();
        if (request.StatusFilter is PaperInstrumentStatus status)
            query = query.Where(c => c.Status == status);

        var checks = query
            .OrderBy(c => c.DueDateUtc)
            .Join(unitOfWork.Repository<LedgerAccount>().Query(),
                c => c.LedgerAccountId,
                a => a.Id,
                (c, a) => new CheckDto(c.Id, c.CheckNumber, a.Name, c.AmountTry, c.DueDateUtc, c.Status))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<CheckDto>>(checks);
    }
}

public sealed record GetPromissoryNotesQuery(PaperInstrumentStatus? StatusFilter = null) : IRequest<IReadOnlyCollection<PromissoryNoteDto>>;

public sealed class GetPromissoryNotesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetPromissoryNotesQuery, IReadOnlyCollection<PromissoryNoteDto>>
{
    public Task<IReadOnlyCollection<PromissoryNoteDto>> Handle(GetPromissoryNotesQuery request, CancellationToken cancellationToken)
    {
        var query = unitOfWork.Repository<PromissoryNote>().Query();
        if (request.StatusFilter is PaperInstrumentStatus status)
            query = query.Where(n => n.Status == status);

        var notes = query
            .OrderBy(n => n.DueDateUtc)
            .Join(unitOfWork.Repository<LedgerAccount>().Query(),
                n => n.LedgerAccountId,
                a => a.Id,
                (n, a) => new PromissoryNoteDto(n.Id, n.NoteNumber, a.Name, n.AmountTry, n.DueDateUtc, n.Status))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<PromissoryNoteDto>>(notes);
    }
}
