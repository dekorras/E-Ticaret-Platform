using Dekorras.Application.Common.Interfaces;
using Dekorras.Application.Content.Models;
using Dekorras.Domain.Content;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Content.Commands;

/// <summary>Bir bölgeye (kök) ya da bir Column düğümünün içine yeni bir Row ekler.</summary>
public sealed record AddBannerRowCommand(Guid BannerZoneId, Guid? ParentColumnId, string? SettingsJson) : IRequest<Guid>;

public sealed class AddBannerRowCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddBannerRowCommand, Guid>
{
    public async Task<Guid> Handle(AddBannerRowCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BannerNode>();
        BannerNode? parent = null;

        if (request.ParentColumnId is not null)
        {
            parent = repository.Query().FirstOrDefault(n => n.Id == request.ParentColumnId)
                ?? throw new KeyNotFoundException($"'{request.ParentColumnId}' numaralı düğüm bulunamadı.");
            if (parent.NodeType != BannerNodeType.Column)
                throw new InvalidOperationException("Bir satır yalnızca bir kolonun içine veya bölgenin köküne eklenebilir.");
            if (parent.Depth >= 5)
                throw new InvalidOperationException("Maksimum iç içe geçme derinliğine (6) ulaşıldı.");
        }

        var siblingCount = repository.Query().Count(n => n.BannerZoneId == request.BannerZoneId && n.ParentId == request.ParentColumnId);
        var depth = parent is null ? 0 : parent.Depth + 1;

        var row = new BannerNode(request.BannerZoneId, request.ParentColumnId, BannerNodeType.Row, siblingCount, depth, path: "");
        row.SetSettings(request.SettingsJson);

        await repository.AddAsync(row, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Path, kendi Id'sini içerdiği için ancak Id üretildikten (ctor'da BaseEntity tarafından)
        // sonra hesaplanabilir - ilk SaveChanges'ten sonra Path'i düzeltip ikinci kez kaydediyoruz.
        row.Reparent(row.ParentId, BuildPath(parent?.Path, row.Id), depth, siblingCount);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return row.Id;
    }

    internal static string BuildPath(string? parentPath, Guid id) => (parentPath ?? "/") + id + "/";
}

/// <summary>Var olan bir Row düğümünü hazır bir genişlik şablonuyla (ör. [6,6] veya [4,4,4]) N adet Column'a böler.</summary>
public sealed record SplitRowIntoColumnsCommand(Guid RowId, int[] ColumnWidthsXs) : IRequest<Unit>;

public sealed class SplitRowIntoColumnsCommandValidator : AbstractValidator<SplitRowIntoColumnsCommand>
{
    public SplitRowIntoColumnsCommandValidator()
    {
        RuleFor(x => x.ColumnWidthsXs).NotEmpty();
        RuleForEach(x => x.ColumnWidthsXs).InclusiveBetween(1, 12);
        RuleFor(x => x.ColumnWidthsXs).Must(w => w.Sum() <= 12).WithMessage("Kolon genişlikleri toplamı 12'yi geçemez.");
    }
}

public sealed class SplitRowIntoColumnsCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SplitRowIntoColumnsCommand, Unit>
{
    public async Task<Unit> Handle(SplitRowIntoColumnsCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BannerNode>();
        var row = repository.Query().FirstOrDefault(n => n.Id == request.RowId)
            ?? throw new KeyNotFoundException($"'{request.RowId}' numaralı satır bulunamadı.");
        if (row.NodeType != BannerNodeType.Row)
            throw new InvalidOperationException("Yalnızca bir satır kolonlara bölünebilir.");

        var existingColumns = repository.Query().Where(n => n.ParentId == row.Id).ToList();
        foreach (var existing in existingColumns) repository.Remove(existing);

        for (var i = 0; i < request.ColumnWidthsXs.Length; i++)
        {
            var column = new BannerNode(row.BannerZoneId, row.Id, BannerNodeType.Column, i, row.Depth + 1,
                AddBannerRowCommandHandler.BuildPath(row.Path, Guid.Empty));
            var settings = new BannerNodeSettings { ColXs = request.ColumnWidthsXs[i] };
            column.SetSettings(BannerSettingsJson.Serialize(settings));
            await repository.AddAsync(column, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Path'leri gerçek üretilen Id'lerle düzelt (bkz. AddBannerRowCommandHandler'daki aynı iki-aşamalı desen).
        foreach (var column in repository.Query().Where(n => n.ParentId == row.Id))
            column.Reparent(row.Id, AddBannerRowCommandHandler.BuildPath(row.Path, column.Id), row.Depth + 1, column.SortOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record UpdateBannerNodeSettingsCommand(Guid Id, string? SettingsJson) : IRequest<Unit>;

public sealed class UpdateBannerNodeSettingsCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateBannerNodeSettingsCommand, Unit>
{
    public async Task<Unit> Handle(UpdateBannerNodeSettingsCommand request, CancellationToken cancellationToken)
    {
        var node = await unitOfWork.Repository<BannerNode>().GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı düğüm bulunamadı.");

        node.SetSettings(request.SettingsJson);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record UpdateBannerNodeAdvancedCommand(Guid Id, string? CustomCssClass, string? CustomId, bool IsActive) : IRequest<Unit>;

public sealed class UpdateBannerNodeAdvancedCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateBannerNodeAdvancedCommand, Unit>
{
    public async Task<Unit> Handle(UpdateBannerNodeAdvancedCommand request, CancellationToken cancellationToken)
    {
        var node = await unitOfWork.Repository<BannerNode>().GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı düğüm bulunamadı.");

        node.SetCustomAttributes(request.CustomCssClass, request.CustomId);
        if (request.IsActive) node.Activate(); else node.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Sürükle-bırak ile bir düğümü yeni bir ebeveynin altına ve/veya yeni bir sıraya taşır.</summary>
public sealed record MoveBannerNodeCommand(Guid Id, Guid? NewParentId, int NewSortOrder) : IRequest<Unit>;

public sealed class MoveBannerNodeCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MoveBannerNodeCommand, Unit>
{
    public async Task<Unit> Handle(MoveBannerNodeCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BannerNode>();
        var node = repository.Query().FirstOrDefault(n => n.Id == request.Id)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı düğüm bulunamadı.");

        BannerNode? newParent = null;
        if (request.NewParentId is not null)
        {
            newParent = repository.Query().FirstOrDefault(n => n.Id == request.NewParentId)
                ?? throw new KeyNotFoundException($"'{request.NewParentId}' numaralı hedef düğüm bulunamadı.");

            if (newParent.Path.StartsWith(node.Path, StringComparison.Ordinal))
                throw new InvalidOperationException("Bir düğüm kendi alt ağacının içine taşınamaz.");

            var expectedChildType = newParent.NodeType == BannerNodeType.Row ? BannerNodeType.Column : BannerNodeType.Row;
            if (node.NodeType != expectedChildType)
                throw new InvalidOperationException("Satırlar yalnızca kolonların, kolonlar yalnızca satırların içine taşınabilir.");
        }

        var newDepth = newParent is null ? 0 : newParent.Depth + 1;
        var newPath = AddBannerRowCommandHandler.BuildPath(newParent?.Path, node.Id);
        node.Reparent(request.NewParentId, newPath, newDepth, request.NewSortOrder);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Bir düğümü ve TÜM alt ağacını (Path LIKE ile) siler - ilişkili içerikler DB cascade ile birlikte gider.</summary>
public sealed record DeleteBannerNodeSubtreeCommand(Guid Id) : IRequest<Unit>;

public sealed class DeleteBannerNodeSubtreeCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<DeleteBannerNodeSubtreeCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBannerNodeSubtreeCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BannerNode>();
        var node = repository.Query().FirstOrDefault(n => n.Id == request.Id);
        if (node is null) return Unit.Value;

        var subtree = repository.Query()
            .Where(n => n.BannerZoneId == node.BannerZoneId && n.Path.StartsWith(node.Path))
            .ToList();

        foreach (var descendant in subtree) repository.Remove(descendant);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
