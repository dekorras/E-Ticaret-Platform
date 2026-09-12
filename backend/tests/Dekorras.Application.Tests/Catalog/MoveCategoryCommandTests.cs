using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentAssertions;
using NSubstitute;

namespace Dekorras.Application.Tests.Catalog;

public class MoveCategoryCommandTests
{
    [Fact]
    public async Task Handle_YukariTasima_KardesIleDisplayOrderiTakasEder()
    {
        var parentId = (Guid?)null;
        var first = new Category("first", parentId, displayOrder: 0);
        var second = new Category("second", parentId, displayOrder: 1);
        var categories = new List<Category> { first, second };

        var repository = Substitute.For<IRepository<Category>>();
        repository.Query().Returns(_ => categories.AsQueryable());
        repository.GetByIdAsync(second.Id, Arg.Any<CancellationToken>()).Returns(second);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Repository<Category>().Returns(repository);

        var handler = new MoveCategoryCommandHandler(unitOfWork);

        await handler.Handle(new MoveCategoryCommand(second.Id, MoveDirection.Up), CancellationToken.None);

        second.DisplayOrder.Should().Be(0);
        first.DisplayOrder.Should().Be(1);
    }

    [Fact]
    public async Task Handle_EnUsttekiKategoriYukariTasinamaz_SiralamaDegismez()
    {
        var first = new Category("first", null, displayOrder: 0);
        var second = new Category("second", null, displayOrder: 1);
        var categories = new List<Category> { first, second };

        var repository = Substitute.For<IRepository<Category>>();
        repository.Query().Returns(_ => categories.AsQueryable());
        repository.GetByIdAsync(first.Id, Arg.Any<CancellationToken>()).Returns(first);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Repository<Category>().Returns(repository);

        var handler = new MoveCategoryCommandHandler(unitOfWork);

        await handler.Handle(new MoveCategoryCommand(first.Id, MoveDirection.Up), CancellationToken.None);

        first.DisplayOrder.Should().Be(0);
        second.DisplayOrder.Should().Be(1);
    }
}
